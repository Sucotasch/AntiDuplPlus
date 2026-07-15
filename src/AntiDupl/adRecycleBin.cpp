/*
* AntiDupl.NET Program (http://ermig1979.github.io/AntiDupl).
*
* Copyright (c) 2002-2018 Yermalayeu Ihar.
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
#include "adFileUtils.h"
#include "adEngine.h"
#include "adOptions.h"
#include "adStatus.h"
#include "adImageInfo.h"
#include "adRecycleBin.h"

namespace ad
{
    TRecycleBin::TRecycleBin(TEngine *pEngine)
        :m_pOptions(pEngine->Options()),
        m_pStatus(pEngine->Status())
    {
    }

    TRecycleBin::~TRecycleBin()
    {
    }

    bool TRecycleBin::Delete(TImageInfo *pImageInfo)
    {
        if(pImageInfo->removed || !IsFileExists(pImageInfo->path.Original().c_str()))
            return true;

        if(FileDelete(pImageInfo->path.Original().c_str(), m_pOptions->advanced.deleteToRecycleBin == TRUE))
        {
            pImageInfo->removed = true;
            m_pStatus->DeleteImage(1, pImageInfo->size);
            return true;
        }
        return false;
    }

    bool TRecycleBin::Restore(TImageInfo *pImageInfo)
    {
        // Files are deleted immediately to Recycle Bin.
        // Restore is not supported — user recovers from Recycle Bin manually.
        return false;
    }

    bool TRecycleBin::Free(TImageInfo *pImageInfo)
    {
        // No temp files to free — deletion is immediate.
        return false;
    }

    bool TRecycleBin::ClearTemporaryFiles(bool permanent)
    {
        // No temp files — deletion is immediate.
        return true;
    }
}
