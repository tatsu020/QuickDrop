#include <windows.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <strsafe.h>

#include <algorithm>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

#pragma comment(lib, "shlwapi.lib")

namespace
{
    // {9B75B6F7-8C63-4B52-A9E4-2CF777E83456}
    const CLSID CLSID_QuickDropExplorerCommand =
    { 0x9b75b6f7, 0x8c63, 0x4b52, { 0xa9, 0xe4, 0x2c, 0xf7, 0x77, 0xe8, 0x34, 0x56 } };

    HINSTANCE g_instance = nullptr;
    long g_dllRefCount = 0;

    struct Peer
    {
        std::wstring id;
        std::wstring title;
        std::wstring source;
        std::wstring endpoint;
    };

    void SafeRelease(IUnknown* value)
    {
        if (value)
        {
            value->Release();
        }
    }

    std::wstring Utf8ToWide(const std::string& value)
    {
        if (value.empty())
        {
            return {};
        }

        const int size = MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0);
        if (size <= 0)
        {
            return {};
        }

        std::wstring result(size, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), size);
        return result;
    }

    std::string WideToUtf8(const std::wstring& value)
    {
        if (value.empty())
        {
            return {};
        }

        const int size = WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
        if (size <= 0)
        {
            return {};
        }

        std::string result(size, '\0');
        WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), size, nullptr, nullptr);
        return result;
    }

    std::string UnescapeField(const std::string& value)
    {
        std::string result;
        result.reserve(value.size());
        for (size_t i = 0; i < value.size(); ++i)
        {
            if (value[i] == '\\' && i + 1 < value.size())
            {
                ++i;
                switch (value[i])
                {
                case 't': result.push_back('\t'); break;
                case 'r': result.push_back('\r'); break;
                case 'n': result.push_back('\n'); break;
                case '\\': result.push_back('\\'); break;
                default: result.push_back(value[i]); break;
                }
            }
            else
            {
                result.push_back(value[i]);
            }
        }

        return result;
    }

    std::vector<std::string> SplitTab(const std::string& line)
    {
        std::vector<std::string> fields;
        size_t start = 0;
        while (start <= line.size())
        {
            const size_t tab = line.find('\t', start);
            if (tab == std::string::npos)
            {
                fields.push_back(line.substr(start));
                break;
            }

            fields.push_back(line.substr(start, tab - start));
            start = tab + 1;
        }

        return fields;
    }

    std::wstring GetLocalAppDataPath()
    {
        PWSTR path = nullptr;
        std::wstring result;
        if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &path)))
        {
            result = path;
            CoTaskMemFree(path);
        }

        return result;
    }

    std::wstring CombinePath(const std::wstring& left, const std::wstring& right)
    {
        wchar_t buffer[MAX_PATH * 2] = {};
        StringCchCopyW(buffer, ARRAYSIZE(buffer), left.c_str());
        PathAppendW(buffer, right.c_str());
        return buffer;
    }

    std::wstring GetPeersMenuPath()
    {
        auto localAppData = GetLocalAppDataPath();
        if (localAppData.empty())
        {
            return {};
        }

        return CombinePath(CombinePath(localAppData, L"QuickDrop"), L"peers-menu.tsv");
    }

    std::wstring GetModuleDirectory()
    {
        wchar_t modulePath[MAX_PATH * 2] = {};
        GetModuleFileNameW(g_instance, modulePath, ARRAYSIZE(modulePath));
        PathRemoveFileSpecW(modulePath);
        return modulePath;
    }

    std::wstring ReadRegistryString(HKEY root, const wchar_t* subKey, const wchar_t* valueName)
    {
        HKEY key = nullptr;
        if (RegOpenKeyExW(root, subKey, 0, KEY_READ, &key) != ERROR_SUCCESS)
        {
            return {};
        }

        wchar_t buffer[MAX_PATH * 4] = {};
        DWORD type = REG_SZ;
        DWORD size = sizeof(buffer);
        const LONG result = RegGetValueW(key, nullptr, valueName, RRF_RT_REG_SZ, &type, buffer, &size);
        RegCloseKey(key);
        return result == ERROR_SUCCESS ? std::wstring(buffer) : std::wstring();
    }

    std::wstring GetCliPath()
    {
        auto path = ReadRegistryString(HKEY_CURRENT_USER, L"Software\\QuickDrop", L"CliPath");
        if (!path.empty())
        {
            return path;
        }

        return CombinePath(GetModuleDirectory(), L"QuickDrop.Cli.exe");
    }

    std::wstring GetAppPath()
    {
        auto installDirectory = ReadRegistryString(HKEY_CURRENT_USER, L"Software\\QuickDrop", L"InstallDirectory");
        if (!installDirectory.empty())
        {
            return CombinePath(installDirectory, L"QuickDrop.App.exe");
        }

        return CombinePath(GetModuleDirectory(), L"QuickDrop.App.exe");
    }

    std::vector<Peer> LoadPeers()
    {
        std::vector<Peer> peers;
        const auto path = GetPeersMenuPath();
        if (path.empty())
        {
            return peers;
        }

        std::ifstream input(path, std::ios::binary);
        if (!input)
        {
            return peers;
        }

        std::stringstream buffer;
        buffer << input.rdbuf();
        std::string data = buffer.str();
        if (data.size() >= 3 &&
            static_cast<unsigned char>(data[0]) == 0xEF &&
            static_cast<unsigned char>(data[1]) == 0xBB &&
            static_cast<unsigned char>(data[2]) == 0xBF)
        {
            data.erase(0, 3);
        }

        std::stringstream lines(data);
        std::string line;
        while (std::getline(lines, line))
        {
            if (!line.empty() && line.back() == '\r')
            {
                line.pop_back();
            }

            const auto fields = SplitTab(line);
            if (fields.size() < 6)
            {
                continue;
            }

            Peer peer;
            peer.id = Utf8ToWide(UnescapeField(fields[0]));
            peer.title = Utf8ToWide(UnescapeField(fields[1]));
            peer.source = Utf8ToWide(UnescapeField(fields[2]));
            peer.endpoint = Utf8ToWide(UnescapeField(fields[3]));
            if (!peer.id.empty() && !peer.title.empty())
            {
                peers.push_back(peer);
            }
        }

        return peers;
    }

    HRESULT DuplicateString(const wchar_t* text, wchar_t** out)
    {
        return SHStrDupW(text, out);
    }

    std::vector<std::wstring> GetSelectedFileSystemPaths(IShellItemArray* items)
    {
        std::vector<std::wstring> paths;
        if (!items)
        {
            return paths;
        }

        DWORD count = 0;
        if (FAILED(items->GetCount(&count)))
        {
            return paths;
        }

        for (DWORD i = 0; i < count; ++i)
        {
            IShellItem* item = nullptr;
            if (FAILED(items->GetItemAt(i, &item)) || !item)
            {
                continue;
            }

            PWSTR path = nullptr;
            if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path)
            {
                paths.emplace_back(path);
                CoTaskMemFree(path);
            }

            item->Release();
        }

        return paths;
    }

    std::wstring CreateTempPathsFile(const std::vector<std::wstring>& paths)
    {
        wchar_t tempPath[MAX_PATH + 1] = {};
        GetTempPathW(ARRAYSIZE(tempPath), tempPath);

        GUID guid = {};
        CoCreateGuid(&guid);
        wchar_t guidText[64] = {};
        StringFromGUID2(guid, guidText, ARRAYSIZE(guidText));

        std::wstring filePath = tempPath;
        filePath += L"QuickDrop-";
        filePath += guidText;
        filePath += L".txt";

        HANDLE file = CreateFileW(filePath.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY, nullptr);
        if (file == INVALID_HANDLE_VALUE)
        {
            return {};
        }

        for (const auto& path : paths)
        {
            auto utf8 = WideToUtf8(path);
            utf8 += "\r\n";
            DWORD written = 0;
            WriteFile(file, utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr);
        }

        CloseHandle(file);
        return filePath;
    }

    void LaunchQuickDropApp()
    {
        const auto appPath = GetAppPath();
        ShellExecuteW(nullptr, L"open", appPath.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
    }

    HRESULT LaunchSendCommand(const std::wstring& peerId, IShellItemArray* items)
    {
        const auto paths = GetSelectedFileSystemPaths(items);
        if (paths.empty())
        {
            return E_FAIL;
        }

        const auto pathsFile = CreateTempPathsFile(paths);
        if (pathsFile.empty())
        {
            return E_FAIL;
        }

        const auto cliPath = GetCliPath();
        std::wstring parameters = L"send --target \"";
        parameters += peerId;
        parameters += L"\" --paths-file \"";
        parameters += pathsFile;
        parameters += L"\" --message-box";

        const auto result = reinterpret_cast<INT_PTR>(ShellExecuteW(nullptr, L"open", cliPath.c_str(), parameters.c_str(), nullptr, SW_HIDE));
        return result > 32 ? S_OK : E_FAIL;
    }

    HRESULT SetStringValue(HKEY root, const std::wstring& keyPath, const wchar_t* name, const std::wstring& value)
    {
        HKEY key = nullptr;
        const auto created = RegCreateKeyExW(root, keyPath.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr);
        if (created != ERROR_SUCCESS)
        {
            return HRESULT_FROM_WIN32(created);
        }

        const DWORD bytes = static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t));
        const auto result = RegSetValueExW(key, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()), bytes);
        RegCloseKey(key);
        return HRESULT_FROM_WIN32(result);
    }

    HRESULT DeleteTree(HKEY root, const std::wstring& keyPath)
    {
        const auto result = RegDeleteTreeW(root, keyPath.c_str());
        if (result == ERROR_FILE_NOT_FOUND)
        {
            return S_OK;
        }

        return HRESULT_FROM_WIN32(result);
    }

    std::wstring GuidToString(REFGUID guid)
    {
        wchar_t text[64] = {};
        StringFromGUID2(guid, text, ARRAYSIZE(text));
        return text;
    }
}

class ExplorerCommand final : public IExplorerCommand
{
public:
    enum class Kind
    {
        Root,
        Peer,
        OpenApp
    };

    ExplorerCommand(Kind kind, Peer peer = {}) : _kind(kind), _peer(std::move(peer))
    {
        InterlockedIncrement(&g_dllRefCount);
    }

    ~ExplorerCommand()
    {
        InterlockedDecrement(&g_dllRefCount);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv)
        {
            return E_POINTER;
        }

        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IExplorerCommand))
        {
            *ppv = static_cast<IExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&_refCount);
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const auto count = InterlockedDecrement(&_refCount);
        if (count == 0)
        {
            delete this;
        }

        return count;
    }

    IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) override
    {
        if (_kind == Kind::Root)
        {
            return DuplicateString(L"\u30D5\u30A1\u30A4\u30EB\u3092\u9001\u4FE1", name);
        }

        if (_kind == Kind::OpenApp)
        {
            return DuplicateString(L"QuickDrop\u3092\u958B\u304F", name);
        }

        return DuplicateString(_peer.title.c_str(), name);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override
    {
        const auto appPath = GetAppPath();
        return appPath.empty() ? E_NOTIMPL : DuplicateString(appPath.c_str(), icon);
    }

    IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override
    {
        if (_kind == Kind::Peer)
        {
            return DuplicateString(_peer.endpoint.c_str(), tip);
        }

        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(GUID* guid) override
    {
        if (!guid)
        {
            return E_POINTER;
        }

        *guid = GUID_NULL;
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override
    {
        if (!state)
        {
            return E_POINTER;
        }

        *state = ECS_ENABLED;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        if (_kind == Kind::OpenApp)
        {
            LaunchQuickDropApp();
            return S_OK;
        }

        if (_kind == Kind::Peer)
        {
            return LaunchSendCommand(_peer.id, items);
        }

        return S_OK;
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override;

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** enumCommands) override;

private:
    long _refCount = 1;
    Kind _kind;
    Peer _peer;
};

class ExplorerCommandEnum final : public IEnumExplorerCommand
{
public:
    explicit ExplorerCommandEnum(std::vector<IExplorerCommand*> commands) : _commands(std::move(commands))
    {
        InterlockedIncrement(&g_dllRefCount);
    }

    ~ExplorerCommandEnum()
    {
        for (auto* command : _commands)
        {
            SafeRelease(command);
        }

        InterlockedDecrement(&g_dllRefCount);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv)
        {
            return E_POINTER;
        }

        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IEnumExplorerCommand))
        {
            *ppv = static_cast<IEnumExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&_refCount);
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const auto count = InterlockedDecrement(&_refCount);
        if (count == 0)
        {
            delete this;
        }

        return count;
    }

    IFACEMETHODIMP Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override
    {
        if (!commands || (count != 1 && !fetched))
        {
            return E_POINTER;
        }

        ULONG copied = 0;
        while (copied < count && _index < _commands.size())
        {
            commands[copied] = _commands[_index];
            commands[copied]->AddRef();
            ++copied;
            ++_index;
        }

        if (fetched)
        {
            *fetched = copied;
        }

        return copied == count ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP Skip(ULONG count) override
    {
        _index = std::min(_index + count, _commands.size());
        return _index < _commands.size() ? S_OK : S_FALSE;
    }

    IFACEMETHODIMP Reset() override
    {
        _index = 0;
        return S_OK;
    }

    IFACEMETHODIMP Clone(IEnumExplorerCommand** clone) override
    {
        if (!clone)
        {
            return E_POINTER;
        }

        std::vector<IExplorerCommand*> copied;
        copied.reserve(_commands.size());
        for (auto* command : _commands)
        {
            command->AddRef();
            copied.push_back(command);
        }

        auto* result = new (std::nothrow) ExplorerCommandEnum(std::move(copied));
        if (!result)
        {
            return E_OUTOFMEMORY;
        }

        result->_index = _index;
        *clone = result;
        return S_OK;
    }

private:
    long _refCount = 1;
    std::vector<IExplorerCommand*> _commands;
    size_t _index = 0;
};

IFACEMETHODIMP ExplorerCommand::GetFlags(EXPCMDFLAGS* flags)
{
    if (!flags)
    {
        return E_POINTER;
    }

    *flags = _kind == Kind::Root ? ECF_HASSUBCOMMANDS : ECF_DEFAULT;
    return S_OK;
}

IFACEMETHODIMP ExplorerCommand::EnumSubCommands(IEnumExplorerCommand** enumCommands)
{
    if (!enumCommands)
    {
        return E_POINTER;
    }

    *enumCommands = nullptr;
    if (_kind != Kind::Root)
    {
        return E_NOTIMPL;
    }

    std::vector<IExplorerCommand*> commands;
    for (const auto& peer : LoadPeers())
    {
        auto* command = new (std::nothrow) ExplorerCommand(Kind::Peer, peer);
        if (command)
        {
            commands.push_back(command);
        }
    }

    if (commands.empty())
    {
        auto* openCommand = new (std::nothrow) ExplorerCommand(Kind::OpenApp);
        if (openCommand)
        {
            commands.push_back(openCommand);
        }
    }

    auto* enumerator = new (std::nothrow) ExplorerCommandEnum(std::move(commands));
    if (!enumerator)
    {
        return E_OUTOFMEMORY;
    }

    *enumCommands = enumerator;
    return S_OK;
}

class ClassFactory final : public IClassFactory
{
public:
    ClassFactory()
    {
        InterlockedIncrement(&g_dllRefCount);
    }

    ~ClassFactory()
    {
        InterlockedDecrement(&g_dllRefCount);
    }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv)
        {
            return E_POINTER;
        }

        *ppv = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory))
        {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return InterlockedIncrement(&_refCount);
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        const auto count = InterlockedDecrement(&_refCount);
        if (count == 0)
        {
            delete this;
        }

        return count;
    }

    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** ppv) override
    {
        if (outer)
        {
            return CLASS_E_NOAGGREGATION;
        }

        auto* command = new (std::nothrow) ExplorerCommand(ExplorerCommand::Kind::Root);
        if (!command)
        {
            return E_OUTOFMEMORY;
        }

        const auto hr = command->QueryInterface(riid, ppv);
        command->Release();
        return hr;
    }

    IFACEMETHODIMP LockServer(BOOL lock) override
    {
        if (lock)
        {
            InterlockedIncrement(&g_dllRefCount);
        }
        else
        {
            InterlockedDecrement(&g_dllRefCount);
        }

        return S_OK;
    }

private:
    long _refCount = 1;
};

extern "C" BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_instance = instance;
        DisableThreadLibraryCalls(instance);
    }

    return TRUE;
}

STDAPI DllCanUnloadNow()
{
    return g_dllRefCount == 0 ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** ppv)
{
    if (!IsEqualCLSID(clsid, CLSID_QuickDropExplorerCommand))
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    auto* factory = new (std::nothrow) ClassFactory();
    if (!factory)
    {
        return E_OUTOFMEMORY;
    }

    const auto hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}

STDAPI DllRegisterServer()
{
    wchar_t modulePath[MAX_PATH * 2] = {};
    GetModuleFileNameW(g_instance, modulePath, ARRAYSIZE(modulePath));
    const auto clsidText = GuidToString(CLSID_QuickDropExplorerCommand);
    const auto clsidKey = L"Software\\Classes\\CLSID\\" + clsidText;
    const auto inprocKey = clsidKey + L"\\InprocServer32";

    HRESULT hr = SetStringValue(HKEY_CURRENT_USER, inprocKey, nullptr, modulePath);
    if (FAILED(hr)) return hr;
    hr = SetStringValue(HKEY_CURRENT_USER, inprocKey, L"ThreadingModel", L"Apartment");
    if (FAILED(hr)) return hr;

    const auto appPath = GetAppPath();
    const std::vector<std::wstring> shellKeys =
    {
        L"Software\\Classes\\*\\shell\\QuickDrop.Send",
        L"Software\\Classes\\Directory\\shell\\QuickDrop.Send"
    };

    for (const auto& key : shellKeys)
    {
        hr = SetStringValue(HKEY_CURRENT_USER, key, L"MUIVerb", L"\u30D5\u30A1\u30A4\u30EB\u3092\u9001\u4FE1");
        if (FAILED(hr)) return hr;
        hr = SetStringValue(HKEY_CURRENT_USER, key, L"Icon", appPath);
        if (FAILED(hr)) return hr;
        hr = SetStringValue(HKEY_CURRENT_USER, key, L"ExplorerCommandHandler", clsidText);
        if (FAILED(hr)) return hr;
    }

    return S_OK;
}

STDAPI DllUnregisterServer()
{
    const auto clsidText = GuidToString(CLSID_QuickDropExplorerCommand);
    DeleteTree(HKEY_CURRENT_USER, L"Software\\Classes\\CLSID\\" + clsidText);
    DeleteTree(HKEY_CURRENT_USER, L"Software\\Classes\\*\\shell\\QuickDrop.Send");
    DeleteTree(HKEY_CURRENT_USER, L"Software\\Classes\\Directory\\shell\\QuickDrop.Send");
    return S_OK;
}
