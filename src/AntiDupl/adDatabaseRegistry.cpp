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
        if (userPath.empty()) {
            wchar_t buffer[MAX_PATH];
            GetModuleFileNameW(NULL, buffer, MAX_PATH);
            std::wstring path(buffer);
            size_t pos = path.find_last_of(L"\\/");
            if (pos != std::wstring::npos) {
                return path.substr(0, pos) + L"\\ad_database.xml";
            }
            return L"ad_database.xml";
        }
        return userPath + L"\\ad_database.xml";
    }

    // Simple XML attribute extractor
    static std::wstring GetXmlAttr(const std::wstring& tag, const std::wstring& attr) {
        std::wstring searchStr = attr + L"=\"";
        size_t pos = tag.find(searchStr);
        if (pos == std::wstring::npos) return L"";
        size_t start = pos + searchStr.length();
        size_t end = tag.find(L"\"", start);
        if (end == std::wstring::npos) return L"";
        return tag.substr(start, end - start);
    }

    bool TDatabaseRegistry::Load(std::vector<TDatabaseInfo>& databases, const std::wstring& userPath) {
        databases.clear();
        std::wifstream file(GetRegistryFilePath(userPath));
        if (!file.is_open()) return false;

        std::wstring line;
        while (std::getline(file, line)) {
            // Trim whitespace
            line.erase(0, line.find_first_not_of(L" \t\r\n"));
            line.erase(line.find_last_not_of(L" \t\r\n") + 1);

            if (line.find(L"<Database ") == 0 || line.find(L"<Database\t") == 0) {
                TDatabaseInfo db;
                db.Path = GetXmlAttr(line, L"Path");
                db.Folder = GetXmlAttr(line, L"Folder");
                db.Name = GetXmlAttr(line, L"Name");
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
        }
        return true;
    }

    bool TDatabaseRegistry::Save(const std::vector<TDatabaseInfo>& databases, const std::wstring& userPath) {
        std::wofstream file(GetRegistryFilePath(userPath));
        if (!file.is_open()) return false;

        file << L"<DatabaseRegistry>\n";
        for (const auto& db : databases) {
            file << L"  <Database Path=\"" << db.Path << L"\"";
            if (!db.Folder.empty()) file << L" Folder=\"" << db.Folder << L"\"";
            if (!db.Name.empty()) file << L" Name=\"" << db.Name << L"\"";
            file << L" Enabled=\"" << (db.Enabled ? L"true" : L"false") << L"\"";
            file << L" ThumbSize=\"" << db.ThumbSize << L"\" Count=\"" << db.ImageCount
                 << L"\" Status=\"" << db.Status << L"\"";
            if (db.Pool != 0) file << L" Pool=\"" << db.Pool << L"\"";
            file << L"/>\n";
        }
        file << L"</DatabaseRegistry>\n";
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

            if (searchPath.find(dbPath) == 0 || dbPath.find(searchPath) == 0) {
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
