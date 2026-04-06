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
#ifndef __adNvJpeg_h__
#define __adNvJpeg_h__

#include "adImage.h"

#ifdef AD_NVJPEG_ENABLE
namespace ad
{
    class TNvJpeg : public TImage
    {
    public:
        static TNvJpeg * Load(HGLOBAL hGlobal, int targetSize = 0);
        static bool Supported(HGLOBAL hGlobal);
        static bool IsAvailable();  // Check if nvJPEG is loaded and initialized
        
        int OriginalWidth() const { return m_origWidth; }
        int OriginalHeight() const { return m_origHeight; }
        
    private:
        int m_origWidth = 0;
        int m_origHeight = 0;
    };
}
#endif//AD_NVJPEG_ENABLE

#endif//__adNvJpeg_h__
