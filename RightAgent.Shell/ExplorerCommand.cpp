#define NOMINMAX

#include "ShellExports.h"

#include "NativeSettings.h"
#include "ProcessHelpers.h"

#include <windows.h>
#include <shobjidl.h>
#include <shlguid.h>
#include <shlwapi.h>
#include <servprov.h>

#include <algorithm>
#include <atomic>
#include <iterator>
#include <filesystem>
#include <new>
#include <optional>
#include <string>
#include <utility>
#include <vector>

namespace
{
    HMODULE g_module = nullptr;
    std::atomic<long> g_moduleReferences = 0;

    template <typename Action>
    HRESULT ComGuard(Action&& action) noexcept
    {
        try
        {
            return action();
        }
        catch (const std::bad_alloc&)
        {
            return E_OUTOFMEMORY;
        }
        catch (...)
        {
            return E_FAIL;
        }
    }

    void AddModuleReference() noexcept
    {
        ++g_moduleReferences;
    }

    void ReleaseModuleReference() noexcept
    {
        --g_moduleReferences;
    }

    std::wstring RootTitle(const rightagent::Settings& settings)
    {
        if (settings.menuMode == rightagent::MenuMode::Direct)
        {
            if (const auto* agent = rightagent::FindDirectAgent(settings))
            {
                return rightagent::IsChinese(settings) ? L"使用 " + agent->name + L" 打开" : L"Open with " + agent->name;
            }
        }
        return rightagent::IsChinese(settings) ? L"使用 RightAgent 打开" : L"Open with RightAgent";
    }

    std::wstring DirectTitle(const rightagent::Settings& settings, const rightagent::AgentDefinition& agent)
    {
        return rightagent::IsChinese(settings) ? L"使用 " + agent.name + L" 打开" : L"Open with " + agent.name;
    }

    const rightagent::AgentDefinition* EnabledAgentAt(
        const rightagent::Settings& settings,
        const std::size_t index)
    {
        std::size_t enabledIndex = 0;
        for (const auto& agent : settings.agents)
        {
            if (!agent.enabled)
            {
                continue;
            }
            if (enabledIndex == index)
            {
                return &agent;
            }
            ++enabledIndex;
        }
        return nullptr;
    }

    bool IsRootSlotVisible(const rightagent::Settings& settings, const std::size_t rootSlot)
    {
        if (!settings.menuEnabled)
        {
            return false;
        }
        if (settings.menuMode == rightagent::MenuMode::MultiDirect)
        {
            return EnabledAgentAt(settings, rootSlot) != nullptr;
        }
        if (rootSlot != 0)
        {
            return false;
        }
        if (settings.menuMode == rightagent::MenuMode::Direct)
        {
            return rightagent::FindDirectAgent(settings) != nullptr;
        }
        return std::any_of(settings.agents.begin(), settings.agents.end(), [](const auto& candidate)
        {
            return candidate.enabled;
        });
    }

    GUID CanonicalGuidForAgent(const std::wstring& id)
    {
        constexpr unsigned long long offset = 14695981039346656037ull;
        constexpr unsigned long long prime = 1099511628211ull;
        unsigned long long hash = offset;
        for (const wchar_t character : id)
        {
            hash ^= static_cast<unsigned long long>(character);
            hash *= prime;
        }

        GUID guid = CLSID_RightAgentExplorerCommand;
        guid.Data1 ^= static_cast<unsigned long>(hash & 0xffffffffull);
        guid.Data2 ^= static_cast<unsigned short>((hash >> 32) & 0xffffull);
        guid.Data3 = static_cast<unsigned short>((guid.Data3 ^ ((hash >> 48) & 0x0fffull)) | 0x5000);
        guid.Data4[0] = static_cast<unsigned char>((guid.Data4[0] & 0x3f) | 0x80);
        return guid;
    }

    HRESULT GetFileSystemFolderFromItem(IShellItem* item, std::filesystem::path& folder, const bool verifyExists)
    {
        if (item == nullptr)
        {
            return E_INVALIDARG;
        }

        SFGAOF attributes = 0;
        HRESULT result = item->GetAttributes(SFGAO_FOLDER | SFGAO_FILESYSTEM, &attributes);
        if (FAILED(result) || (attributes & (SFGAO_FOLDER | SFGAO_FILESYSTEM)) != (SFGAO_FOLDER | SFGAO_FILESYSTEM))
        {
            return HRESULT_FROM_WIN32(ERROR_DIRECTORY);
        }

        PWSTR rawPath = nullptr;
        result = item->GetDisplayName(SIGDN_FILESYSPATH, &rawPath);
        if (FAILED(result))
        {
            return result;
        }
        if (PathIsUNCW(rawPath) || PathIsNetworkPathW(rawPath))
        {
            CoTaskMemFree(rawPath);
            return HRESULT_FROM_WIN32(ERROR_DIRECTORY);
        }
        folder = rawPath;
        CoTaskMemFree(rawPath);
        if (!verifyExists)
        {
            return S_OK;
        }

        std::error_code error;
        return std::filesystem::is_directory(folder, error) ? S_OK : HRESULT_FROM_WIN32(ERROR_DIRECTORY);
    }

    HRESULT GetFolderFromSite(IUnknown* site, std::filesystem::path& folder, const bool verifyExists)
    {
        if (site == nullptr)
        {
            return E_NOINTERFACE;
        }

        IServiceProvider* serviceProvider = nullptr;
        HRESULT result = site->QueryInterface(IID_PPV_ARGS(&serviceProvider));
        if (FAILED(result))
        {
            return result;
        }

        IFolderView* folderView = nullptr;
        result = serviceProvider->QueryService(SID_SFolderView, IID_PPV_ARGS(&folderView));
        serviceProvider->Release();
        if (FAILED(result))
        {
            return result;
        }

        IShellItem* folderItem = nullptr;
        result = folderView->GetFolder(IID_PPV_ARGS(&folderItem));
        folderView->Release();
        if (FAILED(result))
        {
            return result;
        }

        result = GetFileSystemFolderFromItem(folderItem, folder, verifyExists);
        folderItem->Release();
        return result;
    }

    HRESULT ResolveTargetFolder(
        IShellItemArray* selection,
        IUnknown* site,
        std::filesystem::path& folder,
        const bool verifyExists)
    {
        if (selection != nullptr)
        {
            DWORD count = 0;
            HRESULT result = selection->GetCount(&count);
            if (FAILED(result))
            {
                return result;
            }
            if (count > 1)
            {
                return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
            }
            if (count == 1)
            {
                IShellItem* item = nullptr;
                result = selection->GetItemAt(0, &item);
                if (FAILED(result))
                {
                    return result;
                }
                result = GetFileSystemFolderFromItem(item, folder, verifyExists);
                item->Release();
                return result;
            }
        }
        return GetFolderFromSite(site, folder, verifyExists);
    }

    class ExplorerCommand;

    class ExplorerCommandEnumerator final : public IEnumExplorerCommand
    {
    public:
        ExplorerCommandEnumerator(
            std::vector<rightagent::AgentDefinition> agents,
            IUnknown* site,
            const bool useDirectTitles,
            const std::size_t index = 0)
            : agents_(std::move(agents)), site_(site), useDirectTitles_(useDirectTitles), index_(index)
        {
            AddModuleReference();
            if (site_ != nullptr)
            {
                site_->AddRef();
            }
        }

        HRESULT STDMETHODCALLTYPE QueryInterface(const IID& iid, void** object) override
        {
            if (object == nullptr)
            {
                return E_POINTER;
            }
            *object = nullptr;
            if (iid == IID_IUnknown || iid == IID_IEnumExplorerCommand)
            {
                *object = static_cast<IEnumExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        ULONG STDMETHODCALLTYPE AddRef() override
        {
            return ++references_;
        }

        ULONG STDMETHODCALLTYPE Release() override
        {
            const ULONG value = --references_;
            if (value == 0)
            {
                delete this;
            }
            return value;
        }

        HRESULT STDMETHODCALLTYPE Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override;

        HRESULT STDMETHODCALLTYPE Skip(const ULONG count) override
        {
            const auto remaining = agents_.size() - (std::min)(index_, agents_.size());
            const auto skipped = (std::min)(static_cast<std::size_t>(count), remaining);
            index_ += skipped;
            return skipped == static_cast<std::size_t>(count) ? S_OK : S_FALSE;
        }

        HRESULT STDMETHODCALLTYPE Reset() override
        {
            index_ = 0;
            return S_OK;
        }

        HRESULT STDMETHODCALLTYPE Clone(IEnumExplorerCommand** result) override
        {
            if (result == nullptr)
            {
                return E_POINTER;
            }
            *result = new (std::nothrow) ExplorerCommandEnumerator(agents_, site_, useDirectTitles_, index_);
            return *result == nullptr ? E_OUTOFMEMORY : S_OK;
        }

    private:
        ~ExplorerCommandEnumerator()
        {
            if (site_ != nullptr)
            {
                site_->Release();
            }
            ReleaseModuleReference();
        }

        std::atomic<ULONG> references_{1};
        std::vector<rightagent::AgentDefinition> agents_;
        IUnknown* site_{};
        bool useDirectTitles_{};
        std::size_t index_{};
    };

    class ExplorerCommand final : public IExplorerCommand, public IObjectWithSite
    {
    public:
        explicit ExplorerCommand(const std::size_t rootSlot)
            : rootSlot_(rootSlot)
        {
            AddModuleReference();
        }

        ExplorerCommand(rightagent::AgentDefinition agent, IUnknown* site, const bool useDirectTitle)
            : agent_(std::move(agent)), useDirectTitle_(useDirectTitle), site_(site)
        {
            AddModuleReference();
            if (site_ != nullptr)
            {
                site_->AddRef();
            }
        }

        HRESULT STDMETHODCALLTYPE QueryInterface(const IID& iid, void** object) override
        {
            if (object == nullptr)
            {
                return E_POINTER;
            }
            *object = nullptr;
            if (iid == IID_IUnknown || iid == IID_IExplorerCommand)
            {
                *object = static_cast<IExplorerCommand*>(this);
            }
            else if (iid == IID_IObjectWithSite)
            {
                *object = static_cast<IObjectWithSite*>(this);
            }
            else
            {
                return E_NOINTERFACE;
            }
            AddRef();
            return S_OK;
        }

        ULONG STDMETHODCALLTYPE AddRef() override
        {
            return ++references_;
        }

        ULONG STDMETHODCALLTYPE Release() override
        {
            const ULONG value = --references_;
            if (value == 0)
            {
                delete this;
            }
            return value;
        }

        HRESULT STDMETHODCALLTYPE GetTitle(IShellItemArray*, LPWSTR* title) override
        {
            if (title == nullptr)
            {
                return E_POINTER;
            }
            *title = nullptr;
            return ComGuard([&]
            {
                std::wstring text;
                if (agent_)
                {
                    text = useDirectTitle_
                        ? DirectTitle(rightagent::LoadSettings(), *agent_)
                        : agent_->name;
                }
                else
                {
                    const auto settings = rightagent::LoadSettings();
                    if (!IsRootSlotVisible(settings, rootSlot_))
                    {
                        return E_FAIL;
                    }
                    const auto* selectedAgent = ResolveSelectedAgent(settings);
                    text = settings.menuMode == rightagent::MenuMode::MultiDirect && selectedAgent != nullptr
                        ? DirectTitle(settings, *selectedAgent)
                        : RootTitle(settings);
                }
                return text.empty() ? E_FAIL : SHStrDupW(text.c_str(), title);
            });
        }

        HRESULT STDMETHODCALLTYPE GetIcon(IShellItemArray*, LPWSTR* icon) override
        {
            if (icon == nullptr)
            {
                return E_POINTER;
            }
            *icon = nullptr;
            return ComGuard([&]
            {
                const auto settings = rightagent::LoadSettings();
                if (!agent_ && !IsRootSlotVisible(settings, rootSlot_))
                {
                    return E_NOTIMPL;
                }
                std::wstring iconKey = L"builtin:rightagent";
                if (agent_)
                {
                    iconKey = agent_->iconPath;
                }
                else if (const auto* selectedAgent = ResolveSelectedAgent(settings))
                {
                    iconKey = selectedAgent->iconPath;
                }

                const auto path = rightagent::ResolveIconPath(iconKey, rightagent::GetModuleDirectory(g_module));
                std::error_code error;
                return std::filesystem::is_regular_file(path, error) ? SHStrDupW(path.c_str(), icon) : E_NOTIMPL;
            });
        }

        HRESULT STDMETHODCALLTYPE GetToolTip(IShellItemArray*, LPWSTR* tooltip) override
        {
            if (tooltip == nullptr)
            {
                return E_POINTER;
            }
            *tooltip = nullptr;
            return ComGuard([&]
            {
                const auto settings = rightagent::LoadSettings();
                if (agent_)
                {
                    if (!settings.menuEnabled || rightagent::FindEnabledAgent(settings, agent_->id) == nullptr)
                    {
                        return E_FAIL;
                    }
                }
                else if (!IsRootSlotVisible(settings, rootSlot_))
                {
                    return E_FAIL;
                }

                const auto* selectedAgent = ResolveSelectedAgent(settings);
                const auto text = selectedAgent == nullptr
                    ? (rightagent::IsChinese(settings) ? L"在此文件夹中打开编程 Agent" : L"Open a coding agent in this folder")
                    : (rightagent::IsChinese(settings)
                        ? L"在此文件夹中打开 " + selectedAgent->name
                        : L"Open " + selectedAgent->name + L" in this folder");
                return SHStrDupW(text.c_str(), tooltip);
            });
        }

        HRESULT STDMETHODCALLTYPE GetCanonicalName(GUID* commandName) override
        {
            if (commandName == nullptr)
            {
                return E_POINTER;
            }
            *commandName = agent_
                ? CanonicalGuidForAgent(agent_->id)
                : CLSID_RightAgentExplorerCommandSlots[rootSlot_];
            return S_OK;
        }

        HRESULT STDMETHODCALLTYPE GetState(IShellItemArray* selection, BOOL, EXPCMDSTATE* state) override
        {
            if (state == nullptr)
            {
                return E_POINTER;
            }
            *state = ECS_HIDDEN;
            return ComGuard([&]
            {
                const auto settings = rightagent::LoadSettings();
                if (agent_)
                {
                    if (!settings.menuEnabled || rightagent::FindEnabledAgent(settings, agent_->id) == nullptr)
                    {
                        return S_OK;
                    }
                }
                else if (!IsRootSlotVisible(settings, rootSlot_))
                {
                    return S_OK;
                }

                std::filesystem::path folder;
                if (SUCCEEDED(ResolveTargetFolder(selection, site_, folder, false)))
                {
                    *state = ECS_ENABLED;
                }
                return S_OK;
            });
        }

        HRESULT STDMETHODCALLTYPE Invoke(IShellItemArray* selection, IBindCtx*) override
        {
            return ComGuard([&]
            {
                const auto settings = rightagent::LoadSettings();
                if (!settings.menuEnabled)
                {
                    return HRESULT_FROM_WIN32(ERROR_ACCESS_DISABLED_BY_POLICY);
                }
                const auto* selectedAgent = ResolveSelectedAgent(settings);
                if (selectedAgent == nullptr)
                {
                    return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
                }

                std::filesystem::path folder;
                HRESULT result = ResolveTargetFolder(selection, site_, folder, true);
                if (FAILED(result))
                {
                    return result;
                }

                const auto moduleDirectory = rightagent::GetModuleDirectory(g_module);
                const auto launcher = moduleDirectory / L"RightAgent.Launcher.exe";
                DWORD error = ERROR_SUCCESS;
                if (!rightagent::LaunchProcess(
                        launcher,
                        {L"--agent", selectedAgent->id, L"--cwd", folder.wstring()},
                        moduleDirectory,
                        CREATE_NEW_PROCESS_GROUP,
                        &error))
                {
                    return HRESULT_FROM_WIN32(error);
                }
                return S_OK;
            });
        }

        HRESULT STDMETHODCALLTYPE GetFlags(EXPCMDFLAGS* flags) override
        {
            if (flags == nullptr)
            {
                return E_POINTER;
            }
            return ComGuard([&]
            {
                if (agent_)
                {
                    *flags = ECF_DEFAULT;
                    return S_OK;
                }
                const auto settings = rightagent::LoadSettings();
                if (!IsRootSlotVisible(settings, rootSlot_))
                {
                    *flags = ECF_DEFAULT;
                    return S_OK;
                }
                *flags = rootSlot_ == 0 && settings.menuMode == rightagent::MenuMode::Grouped
                    ? ECF_HASSUBCOMMANDS
                    : ECF_DEFAULT;
                return S_OK;
            });
        }

        HRESULT STDMETHODCALLTYPE EnumSubCommands(IEnumExplorerCommand** enumerator) override
        {
            if (enumerator == nullptr)
            {
                return E_POINTER;
            }
            *enumerator = nullptr;
            return ComGuard([&]
            {
                if (agent_ || rootSlot_ != 0)
                {
                    return E_NOTIMPL;
                }

                const auto settings = rightagent::LoadSettings();
                if (settings.menuMode != rightagent::MenuMode::Grouped)
                {
                    return E_NOTIMPL;
                }

                std::vector<rightagent::AgentDefinition> enabled;
                std::copy_if(settings.agents.begin(), settings.agents.end(), std::back_inserter(enabled), [](const auto& agent)
                {
                    return agent.enabled;
                });
                if (enabled.empty())
                {
                    return S_FALSE;
                }
                *enumerator = new (std::nothrow) ExplorerCommandEnumerator(std::move(enabled), site_, false);
                return *enumerator == nullptr ? E_OUTOFMEMORY : S_OK;
            });
        }

        HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) override
        {
            if (site != nullptr)
            {
                site->AddRef();
            }
            if (site_ != nullptr)
            {
                site_->Release();
            }
            site_ = site;
            return S_OK;
        }

        HRESULT STDMETHODCALLTYPE GetSite(const IID& iid, void** site) override
        {
            if (site == nullptr)
            {
                return E_POINTER;
            }
            *site = nullptr;
            return site_ == nullptr ? E_FAIL : site_->QueryInterface(iid, site);
        }

    private:
        [[nodiscard]] const rightagent::AgentDefinition* ResolveSelectedAgent(const rightagent::Settings& settings) const
        {
            if (agent_)
            {
                return rightagent::FindEnabledAgent(settings, agent_->id);
            }
            if (settings.menuMode == rightagent::MenuMode::Direct && rootSlot_ == 0)
            {
                return rightagent::FindDirectAgent(settings);
            }
            return settings.menuMode == rightagent::MenuMode::MultiDirect
                ? EnabledAgentAt(settings, rootSlot_)
                : nullptr;
        }

        ~ExplorerCommand()
        {
            if (site_ != nullptr)
            {
                site_->Release();
            }
            ReleaseModuleReference();
        }

        std::atomic<ULONG> references_{1};
        std::optional<rightagent::AgentDefinition> agent_;
        bool useDirectTitle_{};
        std::size_t rootSlot_{};
        IUnknown* site_{};
    };

    HRESULT ExplorerCommandEnumerator::Next(const ULONG count, IExplorerCommand** commands, ULONG* fetched)
    {
        if (commands == nullptr || (count != 1 && fetched == nullptr))
        {
            return E_POINTER;
        }
        std::fill_n(commands, count, nullptr);
        if (fetched != nullptr)
        {
            *fetched = 0;
        }

        ULONG produced = 0;
        while (produced < count && index_ < agents_.size())
        {
            commands[produced] = new (std::nothrow) ExplorerCommand(agents_[index_], site_, useDirectTitles_);
            if (commands[produced] == nullptr)
            {
                for (ULONG rollback = 0; rollback < produced; ++rollback)
                {
                    commands[rollback]->Release();
                    commands[rollback] = nullptr;
                }
                return E_OUTOFMEMORY;
            }
            ++produced;
            ++index_;
        }
        if (fetched != nullptr)
        {
            *fetched = produced;
        }
        return produced == count ? S_OK : S_FALSE;
    }

    class ClassFactory final : public IClassFactory
    {
    public:
        explicit ClassFactory(const std::size_t rootSlot)
            : rootSlot_(rootSlot)
        {
            AddModuleReference();
        }

        HRESULT STDMETHODCALLTYPE QueryInterface(const IID& iid, void** object) override
        {
            if (object == nullptr)
            {
                return E_POINTER;
            }
            *object = nullptr;
            if (iid == IID_IUnknown || iid == IID_IClassFactory)
            {
                *object = static_cast<IClassFactory*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        ULONG STDMETHODCALLTYPE AddRef() override
        {
            return ++references_;
        }

        ULONG STDMETHODCALLTYPE Release() override
        {
            const ULONG value = --references_;
            if (value == 0)
            {
                delete this;
            }
            return value;
        }

        HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, const IID& iid, void** object) override
        {
            if (outer != nullptr)
            {
                return CLASS_E_NOAGGREGATION;
            }
            if (object == nullptr)
            {
                return E_POINTER;
            }
            *object = nullptr;

            auto* command = new (std::nothrow) ExplorerCommand(rootSlot_);
            if (command == nullptr)
            {
                return E_OUTOFMEMORY;
            }
            const HRESULT result = command->QueryInterface(iid, object);
            command->Release();
            return result;
        }

        HRESULT STDMETHODCALLTYPE LockServer(const BOOL lock) override
        {
            lock ? AddModuleReference() : ReleaseModuleReference();
            return S_OK;
        }

    private:
        ~ClassFactory()
        {
            ReleaseModuleReference();
        }

        std::atomic<ULONG> references_{1};
        std::size_t rootSlot_{};
    };
}

extern "C" BOOL WINAPI DllMain(HINSTANCE instance, const DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}

extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    return g_moduleReferences == 0 ? S_OK : S_FALSE;
}

extern "C" HRESULT __stdcall DllGetClassObject(const CLSID& classId, const IID& iid, void** object)
{
    const auto classIdIterator = std::find(
        CLSID_RightAgentExplorerCommandSlots.begin(),
        CLSID_RightAgentExplorerCommandSlots.end(),
        classId);
    if (classIdIterator == CLSID_RightAgentExplorerCommandSlots.end())
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }
    if (object == nullptr)
    {
        return E_POINTER;
    }
    *object = nullptr;

    const auto rootSlot = static_cast<std::size_t>(
        std::distance(CLSID_RightAgentExplorerCommandSlots.begin(), classIdIterator));
    auto* factory = new (std::nothrow) ClassFactory(rootSlot);
    if (factory == nullptr)
    {
        return E_OUTOFMEMORY;
    }
    const HRESULT result = factory->QueryInterface(iid, object);
    factory->Release();
    return result;
}
