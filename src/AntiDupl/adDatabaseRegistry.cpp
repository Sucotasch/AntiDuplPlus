/*
* AntiDuplPlus Program (http://github.com/Sucotasch/AntiDuplPlus).
*
* Copyright (c) 2023-2026.
*
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
*
* The above copyright notice and this permission notice shall be included in
* all copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
* SOFTWARE.
*/
#include "adDatabaseRegistry.h"
#include <fstream>
#include <sstream>
#include <algorithm>
#include <windows.h>
#include "adFileUtils.h"

namespace ad
{
    // Helper to get the registry file path (portable: exe dir)
    static std::wstring GetRegistryFilePath(const std::wstring& userPath) {
        // Always use exe directory for portable registry (same as C# DatabaseManagerForm)
        wchar_t buffer[MAX_PATH];
        GetModuleFileNameW(NULL, buffer, MAX_PATH);
        std::wstring path(buffer);
        size_t pos = path.find_last_of(L"\\/");
        if (pos != std::wstring::npos) {
            return path.substr(0, pos) + L"\\ad_database.xml";
        }
        return L"ad_database.xml";
    }

    // Inverse of EscapeXmlAttr: decode the five XML entities in attribute values.
    static std::wstring UnescapeXmlAttr(const std::wstring& s) {
        std::wstring out;
        out.reserve(s.size());
        for (size_t i = 0; i < s.size(); ++i) {
            if (s[i] == L'&') {
                if (s.compare(i, 5, L"&amp;") == 0) { out += L'&'; i += 4; continue; }
                if (s.compare(i, 4, L"&lt;") == 0) { out += L'<'; i += 3; continue; }
                if (s.compare(i, 4, L"&gt;") == 0) { out += L'>'; i += 3; continue; }
                if (s.compare(i, 6, L"&quot;") == 0) { out += L'"'; i += 5; continue; }
                if (s.compare(i, 7, L"&apos;") == 0) { out += L'\''; i += 6; continue; }
                // Unknown/bare '&' — keep as-is (legacy unescaped entries).
                out += L'&';
                continue;
            }
            out += s[i];
        }
        return out;
    }

    // Simple XML attribute extractor
    static std::wstring GetXmlAttr(const std::wstring& tag, const std::wstring& attr) {
        std::wstring searchStr = attr + L"=\"";
        size_t pos = tag.find(searchStr);
        if (pos == std::wstring::npos) return L"";
        size_t start = pos + searchStr.length();
        size_t end = tag.find(L"\"", start);
        if (end == std::wstring::npos) return L"";
        std::wstring value = tag.substr(start, end - start);
        // The registry is written by three writers (DLL, C# GUI, collector) and
        // all of them escape XML attribute values; decode entities on read so a
        // round trip through any writer preserves the original path (P1-6).
        return UnescapeXmlAttr(value);
    }

    // Escape XML attribute values (& < > " are the ones that corrupt the file)
    static std::wstring EscapeXmlAttr(const std::wstring& s) {
        std::wstring out;
        out.reserve(s.size());
        for (wchar_t c : s) {
            switch (c) {
            case L'&': out += L"&amp;"; break;
            case L'<': out += L"&lt;"; break;
            case L'>': out += L"&gt;"; break;
            case L'"': out += L"&quot;"; break;
            default: out += c; break;
            }
        }
        return out;
    }

    bool TDatabaseRegistry::Load(std::vector<TDatabaseInfo>& databases, const std::wstring& userPath) {
        databases.clear();
        // P1-8: the registry is UTF-8 (written as UTF-8 by the C# GUI via
        // File.WriteAllText and by the collector via WideCharToMultiByte).
        // The former wide-stream read ran with the C locale and mis-decoded every
        // non-ASCII path (e.g. Cyrillic database names/folders). Read bytes and
        // convert UTF-8 -> UTF-16 explicitly.
        std::ifstream file(GetRegistryFilePath(userPath), std::ios::binary);
        if (!file.is_open()) return false;

        std::string content((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
        std::wstring wcontent;
        if (!content.empty()) {
            int len = MultiByteToWideChar(CP_UTF8, 0, content.c_str(), (int)content.size(), NULL, 0);
            if (len <= 0) return false;
            wcontent.resize(len);
            MultiByteToWideChar(CP_UTF8, 0, content.c_str(), (int)content.size(), &wcontent[0], len);
        }

        size_t lineStart = 0;
        while (lineStart <= wcontent.size()) {
            size_t lineEnd = wcontent.find(L'\n', lineStart);
            std::wstring line = wcontent.substr(lineStart,
                (lineEnd == std::wstring::npos ? wcontent.size() : lineEnd) - lineStart);
            // Trim whitespace
            line.erase(0, line.find_first_not_of(L" \t\r\n"));
            line.erase(line.find_last_not_of(L" \t\r\n") + 1);

            if (line.find(L"<Database ") == 0 || line.find(L"<Database\t") == 0) {
                TDatabaseInfo db;
                db.Path = GetXmlAttr(line, L"Path");
                db.Folder = GetXmlAttr(line, L"Folder");
                db.Name = GetXmlAttr(line, L"Name");
                db.RemapFrom = GetXmlAttr(line, L"RemapFrom");
                std::wstring sizeStr = GetXmlAttr(line, L"ThumbSize");
                try { db.ThumbSize = std::stoi(sizeStr); } catch (...) { db.ThumbSize = 32; }

                std::wstring countStr = GetXmlAttr(line, L"Count");
                try { db.ImageCount = std::stoull(countStr); } catch (...) { db.ImageCount = 0; }

                db.Status = GetXmlAttr(line, L"Status");
                if (db.Status.empty()) db.Status = L"Ready";
                
                std::wstring enabledStr = GetXmlAttr(line, L"Enabled");
                db.Enabled = (enabledStr.empty() || enabledStr == L"true"); // Default to true

                std::wstring poolStr = GetXmlAttr(line, L"Pool");
                try { db.Pool = std::stoi(poolStr); } catch (...) { db.Pool = 0; }

                if (!db.Path.empty()) {
                    databases.push_back(db);
                }
            }

            if (lineEnd == std::wstring::npos) break;
            lineStart = lineEnd + 1;
        }
        return true;
    }

    bool TDatabaseRegistry::Save(const std::vector<TDatabaseInfo>& databases, const std::wstring& userPath) {
        std::wstring filePath = GetRegistryFilePath(userPath);
        std::wstring tmpPath = filePath + L".tmp";

        // P1-8: build the UTF-8 byte buffer in memory (same encoding as the C#
        // GUI and collector writers), then write it atomically.
        std::wstringstream wss;
        wss << L"<DatabaseRegistry>\n";
        for (const auto& db : databases) {
            wss << L"  <Database Path=\"" << EscapeXmlAttr(db.Path) << L"\"";
            if (!db.Folder.empty()) wss << L" Folder=\"" << EscapeXmlAttr(db.Folder) << L"\"";
            if (!db.Name.empty()) wss << L" Name=\"" << EscapeXmlAttr(db.Name) << L"\"";
            if (!db.RemapFrom.empty()) wss << L" RemapFrom=\"" << EscapeXmlAttr(db.RemapFrom) << L"\"";
            wss << L" Enabled=\"" << (db.Enabled ? L"true" : L"false") << L"\"";
            wss << L" ThumbSize=\"" << db.ThumbSize << L"\" Count=\"" << db.ImageCount
                << L"\" Status=\"" << EscapeXmlAttr(db.Status) << L"\"";
            if (db.Pool != 0) wss << L" Pool=\"" << db.Pool << L"\"";
            wss << L"/>\n";
        }
        wss << L"</DatabaseRegistry>\n";

        const std::wstring wcontent = wss.str();
        int len = WideCharToMultiByte(CP_UTF8, 0, wcontent.c_str(), (int)wcontent.size(), NULL, 0, NULL, NULL);
        if (len <= 0) return false;
        std::string utf8(len, 0);
        WideCharToMultiByte(CP_UTF8, 0, wcontent.c_str(), (int)wcontent.size(), &utf8[0], len, NULL, NULL);

        std::ofstream file(tmpPath, std::ios::binary);
        if (!file.is_open()) return false;
        file.write(utf8.c_str(), utf8.size());
        file.flush();
        if (!file.good()) { file.close(); _wremove(tmpPath.c_str()); return false; }
        file.close();

        // Atomic replace: a concurrently-running collector or crash must never leave
        // a truncated registry (both ends rewrite this file).
        if (!MoveFileExW(tmpPath.c_str(), filePath.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
            _wremove(tmpPath.c_str());
            return false;
        }
        return true;
    }

    bool TDatabaseRegistry::AddOrUpdate(const TDatabaseInfo& db, const std::wstring& userPath) {
        std::vector<TDatabaseInfo> databases;
        Load(databases, userPath);

        // Check if exists and update
        bool found = false;
        for (auto& existing : databases) {
            if (existing.Path == db.Path && existing.ThumbSize == db.ThumbSize) {
                existing = db;
                found = true;
                break;
            }
        }

        if (!found) {
            databases.push_back(db);
        }

        return Save(databases, userPath);
    }

    bool TDatabaseRegistry::Remove(const std::wstring& path, const std::wstring& userPath) {
        std::vector<TDatabaseInfo> databases;
        Load(databases, userPath);

        auto it = std::remove_if(databases.begin(), databases.end(), [&path](const TDatabaseInfo& db) {
            return db.Path == path;
        });

        if (it != databases.end()) {
            databases.erase(it, databases.end());
            return Save(databases, userPath);
        }
        return false;
    }

    bool TDatabaseRegistry::FindByPath(const std::wstring& path, TDatabaseInfo& out, const std::wstring& userPath) {
        std::vector<TDatabaseInfo> databases;
        Load(databases, userPath);
        
        // Normalize path for comparison (case-insensitive)
        std::wstring searchPath = path;
        std::transform(searchPath.begin(), searchPath.end(), searchPath.begin(), ::towlower);
        
        for (const auto& db : databases) {
            std::wstring dbPath = db.Path;
            std::transform(dbPath.begin(), dbPath.end(), dbPath.begin(), ::towlower);
            
            if (searchPath == dbPath) {
                out = db;
                return true;
            }
        }
        return false;
    }

    bool TDatabaseRegistry::UpdateCount(const std::wstring& imagePath, int delta, const std::wstring& userPath) {
        std::vector<TDatabaseInfo> databases;
        Load(databases, userPath);

        std::wstring searchPath = imagePath;
        std::transform(searchPath.begin(), searchPath.end(), searchPath.begin(), ::towlower);

        bool found = false;
        for (auto& db : databases) {
            std::wstring dbPath = db.Path;
            std::transform(dbPath.begin(), dbPath.end(), dbPath.begin(), ::towlower);

            if (ad::PathStartsWith(searchPath, dbPath) || ad::PathStartsWith(dbPath, searchPath)) {
                int newCount = (int)db.ImageCount + delta;
                if (newCount < 0) newCount = 0;
                db.ImageCount = (size_t)newCount;
                found = true;
                break;
            }
        }

        return found ? Save(databases, userPath) : false;
    }
}
