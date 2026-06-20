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
#ifndef __adDatabaseRegistry_h__
#define __adDatabaseRegistry_h__

#include <vector>
#include <string>

namespace ad
{
    struct TDatabaseInfo {
        std::wstring Path;      // Search path (e.g. "D:\Photos\Sandy")
        std::wstring Folder;    // Database folder (e.g. "...user\images\32x32")
        std::wstring Name;      // Database name
        int ThumbSize;
        size_t ImageCount;
        std::wstring Status; // Ready, Processing
        bool Enabled;           // Is this database active for search?
        int Pool;               // 0=None, 1=Pool1 (Reference), 2=Pool2 (Target)
    };

    class TDatabaseRegistry {
    public:
        static bool Load(std::vector<TDatabaseInfo>& databases, const std::wstring& userPath);
        static bool Save(const std::vector<TDatabaseInfo>& databases, const std::wstring& userPath);
        static bool AddOrUpdate(const TDatabaseInfo& db, const std::wstring& userPath);
        static bool Remove(const std::wstring& path, const std::wstring& userPath);
        static bool FindByPath(const std::wstring& path, TDatabaseInfo& out, const std::wstring& userPath);
        static bool UpdateCount(const std::wstring& imagePath, int delta, const std::wstring& userPath);
    };
}

#endif // __adDatabaseRegistry_h__
