/*
* AntiDupl.NET Program (http://ermig1979.github.io/AntiDupl).
*
* Copyright (c) 2002-2023 Yermalayeu Ihar.
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
#include "adStatus.h"
#include "adOptions.h"
#include "adImageDataStorage.h"
#include "adIO.h"
#include "adFileStream.h"
#include "adLogger.h"
#include "adException.h"
#include "adGPUManager.h"

namespace ad
{
	const TChar FILE_EXTENSION[] = TEXT(".adi");

    const TChar INDEX_FILE_NAME[] = TEXT("index");
	const TChar BACKUP_FILE_NAME[] = TEXT("backup");

	const char INDEX_CONTROL_BYTES[] = "adii";
	const char DATA_CONTROL_BYTES[] = "adid";

    //-------------------------------------------------------------------------

	TImageDataStorage::TImageDataStorage(TEngine *pEngine)
		:m_pEngine(pEngine),
		m_pStatus(pEngine->Status()),
		m_pOptions(pEngine->Options()), 
		m_needToSave (false),
		m_nextGlobalIdx(0)
	{
	}

	TImageDataStorage::TStorage::iterator TImageDataStorage::Find(const TImageInfo& imageInfo)
	{
		TStorage::iterator it = m_storage.lower_bound(imageInfo.hash);
		TStorage::iterator itEnd = m_storage.upper_bound(imageInfo.hash);
		for(;it != itEnd; ++it)
		{
			if(TPath::EqualByPath(it->second->path, imageInfo.path))
				return it;
		}
		return m_storage.end();
	}

	TImageDataStorage::TStorage::iterator TImageDataStorage::Insert(TImageData* pImageData)
	{
		// Check for globalIdx overflow
		if (m_nextGlobalIdx >= SIZE_MAX) {
#ifdef AD_LOGGER_ENABLE
			AD_LOG("GPU: globalIdx counter overflow, resetting indices...");
#endif
			ResetGpuIndices();
		}

		pImageData->globalIdx = m_nextGlobalIdx++;
		pImageData->pEngine = m_pEngine;
		return m_storage.insert(TStorage::value_type(pImageData->hash, pImageData));
	}

	void TImageDataStorage::ClearMemory()
	{
		for(TStorage::iterator it = m_storage.begin(); it != m_storage.end(); ++it)
			delete it->second;
		m_storage.clear();
		m_nextGlobalIdx = 0;
		if (m_pEngine->GpuManager() && m_pEngine->GpuManager()->IsAvailable())
		{
			m_pEngine->GpuManager()->ClearBuffer();
		}
	}

	void TImageDataStorage::ResetGpuIndices()
	{
		m_nextGlobalIdx = 0;
		for(TStorage::iterator it = m_storage.begin(); it != m_storage.end(); ++it)
		{
			it->second->globalIdx = m_nextGlobalIdx++;
		}
	}

	void TImageDataStorage::Check()
	{
		m_pStatus->Reset();
		size_t size = m_storage.size(), i = 0;
		bool found_deleted = false;
		for(TStorage::iterator it = m_storage.begin(); it != m_storage.end(); )
		{
			if(m_pStatus->Stopped())
				break;

			if(!it->second->Actual(true))
			{
				delete it->second;
				it = m_storage.erase(it);
				found_deleted = true;
			}
			else
				++it;

			m_pStatus->SetProgress(i++, size);
		}
		m_pStatus->Reset();

		// Re-index GPU indices after deletions
		if (found_deleted) {
			ResetGpuIndices();
#ifdef AD_LOGGER_ENABLE
			std::stringstream ss;
			ss << "GPU: Re-indexed " << m_storage.size() << " images after cleanup.";
			AD_LOG(ss.str().c_str());
#endif
		}
	}

	// Загружает в хранилише m_storage переданный файл
	TImageDataPtr TImageDataStorage::Get(const TImageInfo& imageInfo)
	{
		TStorage::iterator it = Find(imageInfo);
		// Если файл найден в хранилише
		if(it != m_storage.end())
		{
			if(it->second->size != imageInfo.size || it->second->time != imageInfo.time)
			{
				delete it->second;
				it->second = new TImageData(imageInfo, m_pOptions->advanced.reducedImageSize);
				m_needToSave = true;
			}
		}
		else
		{
			it = Insert(new TImageData(imageInfo, m_pOptions->advanced.reducedImageSize));
			m_needToSave = true;
		}
		return it->second;
	}

	adError TImageDataStorage::Load(const TChar *path, bool allLoad)
	{
		if(!IsDirectoryExists(path))
			return AD_ERROR_DIRECTORY_IS_NOT_EXIST;

		// Detect format: check first 4 bytes of index.adi
		TString indexPath = CreatePath(path, TString(INDEX_FILE_NAME) + FILE_EXTENSION);
		uint32_t firstBytes = 0;
		bool isDllNative = false;
		
		FILE* f = _wfopen(indexPath.c_str(), L"rb");
		if (f) {
			fread(&firstBytes, 4, 1, f);
			fclose(f);
			// DLL-native format starts with "adii" (0x69696461)
			// Collector-native starts with ThumbSize (e.g., 0x00000020 = 32)
			isDllNative = (firstBytes == 0x69696461u);
		}

		if (isDllNative) {
			// DLL-native format: use existing LoadIndex flow
			TIndex index;
			if( LoadIndex(index, indexPath.c_str(), allLoad) || 
				LoadIndex(index, CreatePath(path, TString(BACKUP_FILE_NAME) + FILE_EXTENSION).c_str(), allLoad))
			{
				size_t size = 0;
				for(TIndex::iterator it = index.begin(); it != index.end(); ++it)
					if(it->second.type == TData::Old)
						size += it->second.size;

				m_pStatus->Reset();
				size_t i = 0;
				for(TIndex::iterator it = index.begin(); it != index.end(); ++it)
				{
					if(m_pStatus->Stopped())
					{
						m_pStatus->Reset();
						return AD_ERROR_UNKNOWN;
					}

					if(it->second.type == TData::Old)
					{
						if (!LoadData(it->second, path, it->second.key))
							return AD_ERROR_UNKNOWN;
						m_pStatus->SetProgress(i, size);
						i += it->second.size;
					}
				}
				m_pStatus->Reset();
				return AD_OK;
			}
			return AD_ERROR_UNKNOWN;
		}
		else
		{
			// Collector-native format (NvJpegCollector): ThumbSize + raw data
			return LoadCollectorNative(path, firstBytes, allLoad);
		}
	}

	adError TImageDataStorage::Save(const TChar *path)
	{
		if(!IsDirectoryExists(path))
			return AD_ERROR_DIRECTORY_IS_NOT_EXIST;

		if (m_needToSave)
		{
			TIndex index;
			if(!LoadIndex(index, CreatePath(path, TString(INDEX_FILE_NAME) + FILE_EXTENSION).c_str()))
				LoadIndex(index, CreatePath(path, TString(BACKUP_FILE_NAME) + FILE_EXTENSION).c_str());
			UpdateIndex(index);
			if(SaveIndex(index, path))
			{
				DeleteOldIndex(index, path);
				::CopyFile(
					CreatePath(path, TString(INDEX_FILE_NAME) + FILE_EXTENSION).c_str(),
					CreatePath(path, TString(BACKUP_FILE_NAME) + FILE_EXTENSION).c_str(),
					FALSE);
				m_needToSave = false;
				return AD_OK;
			}
		}
		
		return AD_OK;
	}

	adError TImageDataStorage::ClearDatabase(const TChar *directory)
	{
		if (Load(directory, true) == AD_OK)
		{
			DeleteFiles(directory, FILE_EXTENSION);
			
			Check();

			TIndex index;
			UpdateIndex(index);

			if(SaveIndex(index, directory))
			{
				ClearMemory();
				return AD_OK;
			}
		}
		ClearMemory();
		return AD_ERROR_UNKNOWN;
	}


	void TImageDataStorage::CreateSorted(TVector & sorted) const
	{
		sorted.clear();

		size_t size = 0;
		for(TStorage::const_iterator it = m_storage.begin(); it != m_storage.end(); ++it)
		{
			if(it->second->NeedToSave()) //data filled
				size++;
		}

		if(size)
		{
			sorted.reserve(size);
			size_t i = 0;
			for(TStorage::const_iterator it = m_storage.begin(); it != m_storage.end(); ++it)
			{
				if(it->second->NeedToSave())
					sorted.push_back(it->second);
			}

			struct TPathComparer
			{
				bool operator ()(const TImageDataPtr pImageData1, const TImageDataPtr pImageData2) const
				{
					return TPath::LesserByPath(pImageData1->path, pImageData2->path);
				}
			};
			std::sort(sorted.begin(), sorted.end(), TPathComparer());
		}
	}

	// Если файлы есть в путях поиска то оставляем в базе.
	void TImageDataStorage::SetOld(TIndex & index, bool allLoad) const
	{
		for(TIndex::iterator it = index.begin(); it != index.end(); ++it)
		{
			TData & data = it->second;
			if (allLoad)
				data.type = TData::Old;
			else
			{
				for(size_t i = 0; i < m_pOptions->searchPaths.Size(); ++i)
				{
					const TPath & path = m_pOptions->searchPaths[i];
					if((!TPath::LesserByPath(path, data.first) && !TPath::BiggerByPath(path, data.last)) 
						|| path.IsSubPath(data.first) || path.IsSubPath(data.last))
					{
						data.type = TData::Old;
						break;
					}
				}
			}
		}
	}

	// Заполняем TData данные для хранения
	void TImageDataStorage::UpdateIndex(TIndex & index) const
	{
		const size_t dataSizeMax = IMAGE_DATA_FILE_SIZE_MAX/
			Simd::Square(m_pOptions->advanced.reducedImageSize/REDUCED_IMAGE_SIZE_MIN);

		TVector sorted;
		CreateSorted(sorted); //создание вектора из заполненных значений в m_storage

		short key = 0;
		for(size_t i = 0; i < sorted.size(); i += dataSizeMax)
		{
			size_t begin = i;
			size_t end = std::min(i + dataSizeMax, sorted.size());

			while(index.count(key)) //находим свободный номер индекса
				key++;

			TData & data = index[key];
			data.type = TData::New;
			data.key = key;
			data.size = end - begin;
			data.first = sorted[begin]->path;
			data.last = sorted[end - 1]->path;
			data.data = TVector(sorted.begin() + begin, sorted.begin() + end);
		}
	}

	//удаляем старый файл хранилища изображений
	void TImageDataStorage::DeleteOldIndex(TIndex & index, const TChar *path) const
	{
		for(TIndex::const_iterator it = index.begin(); it != index.end(); ++it)
		{
			if(it->second.type == TData::Old)
			{
				TString fileName = CreatePath(path, GetDataFileName(it->second.key));
				if(IsFileExists(fileName.c_str()))
				{
					::DeleteFile(fileName.c_str());
				}
			}
		}
	}

	TString TImageDataStorage::GetDataFileName(short key) const
	{
		TStringStream ss;
		ss << std::hex << ((key >> 12)&0xF) << ((key >> 8)&0xF) << ((key >> 4)&0xF) << ((key >> 0)&0xF) << FILE_EXTENSION;
		return ss.str();
	}

	// Сохранение данных в файл
	bool TImageDataStorage::SaveIndex(const TIndex & index, const TChar *path) const
	{
		size_t size = 0, dataSize = 0;
		for(TIndex::const_iterator it = index.begin(); it != index.end(); ++it)
		{
			if(it->second.type == TData::New || it->second.type == TData::Skip)
			{
				size++;
				if(it->second.type == TData::New)
					dataSize += it->second.size;
			}
		}

		bool dataSaveResult = true;
		m_pStatus->Reset();
		try
		{
			TString fileName = CreatePath(path, TString(INDEX_FILE_NAME) + FILE_EXTENSION);
			// Файл индекса
			TOutputFileStream outputFile(fileName.c_str(), INDEX_CONTROL_BYTES);

			outputFile.Save(m_pOptions->advanced.reducedImageSize);
			outputFile.SaveSize(size);

			size_t i = 0;
			for(TIndex::const_iterator it = index.begin(); it != index.end(); ++it)
			{
				if(it->second.type == TData::New || it->second.type == TData::Skip)
				{
					outputFile.Save(it->second.key);
					outputFile.Save(it->second.first);
					outputFile.Save(it->second.last);
					outputFile.SaveSize(it->second.size);
					if(it->second.type == TData::New)
					{
						dataSaveResult = SaveData(it->second, path) && dataSaveResult;
						m_pStatus->SetProgress(i, dataSize);
						i += it->second.size;
					}
				}
			}
		}
		catch (TException e)
		{
			dataSaveResult = e.Error == AD_OK;
		}
		m_pStatus->Reset();

		return dataSaveResult;
	}

	// Сохранение данных о картинках в 0000.adi
	bool TImageDataStorage::SaveData(const TData & data, const TChar *path) const
	{
		try
		{
			TString fileName = CreatePath(path, GetDataFileName(data.key));
			TOutputFileStream outputFile(fileName.c_str(), DATA_CONTROL_BYTES);

			outputFile.Save(m_pOptions->advanced.reducedImageSize);
			outputFile.Save(data.key);
			outputFile.Save(data.first);
			outputFile.Save(data.last);
			// Сохраняем количество изображений
			outputFile.SaveSize(data.size); 
			for(size_t i = 0; i < data.data.size(); ++i)
				outputFile.Save(*data.data[i]);
		}
		catch (TException e)
		{
			return e.Error == AD_OK;
		}
		return true;
	}

	bool TImageDataStorage::LoadIndex(TIndex & index, const TChar *fileName, bool allLoad) const
	{
		try
		{
			TInputFileStream inputFile(fileName, INDEX_CONTROL_BYTES);

			inputFile.Check<TUInt32>(m_pOptions->advanced.reducedImageSize);
			size_t size = inputFile.LoadSizeChecked(SIZE_CHECK_LIMIT);

			index.clear();
			for(size_t i = 0; i < size; i++)
			{
				TData data;
				inputFile.Load(data.key);
				inputFile.Load(data.first);
				inputFile.Load(data.last);
				inputFile.LoadSize(data.size);
				index[data.key] = data;
			}

			SetOld(index, allLoad);
		}
		catch (TException e)
		{
			return e.Error == AD_OK;
		}
		return true;
	}

	// Load Collector-native format (NvJpegCollector): index.adi without "adid" header
	// Format: thumbSize(u32) + groupCount(u64) + key(i16) + first(wstring) + last(wstring) + imgCount(u64)
	adError TImageDataStorage::LoadCollectorNative(const TChar *path, uint32_t thumbSizeFromHeader, bool allLoad)
	{
		TString indexPath = CreatePath(path, TString(INDEX_FILE_NAME) + FILE_EXTENSION);
		
		FILE* f = _wfopen(indexPath.c_str(), L"rb");
		if (!f) return AD_ERROR_UNKNOWN;

		// Skip thumbSize (4 bytes already read)
		fseek(f, 4, SEEK_SET);

		// Read groupCount (u64)
		uint64_t groupCount = 0;
		if (fread(&groupCount, 8, 1, f) != 1) { fclose(f); return AD_ERROR_UNKNOWN; }

		// For each group in index
		for (uint64_t g = 0; g < groupCount; g++)
		{
			// Read key (i16)
			int16_t key = 0;
			if (fread(&key, 2, 1, f) != 1) { fclose(f); return AD_ERROR_UNKNOWN; }

			// Read first path (wstring): length(u64) + wchar_t[length*2]
			uint64_t firstLen = 0;
			if (fread(&firstLen, 8, 1, f) != 1) { fclose(f); return AD_ERROR_UNKNOWN; }
			if (firstLen > 10000) { fclose(f); return AD_ERROR_UNKNOWN; } // sanity check
			std::wstring firstPath(firstLen, L'\0');
			if (firstLen > 0 && fread(&firstPath[0], 2, firstLen, f) != firstLen) { fclose(f); return AD_ERROR_UNKNOWN; }

			// Read last path (wstring)
			uint64_t lastLen = 0;
			if (fread(&lastLen, 8, 1, f) != 1) { fclose(f); return AD_ERROR_UNKNOWN; }
			if (lastLen > 10000) { fclose(f); return AD_ERROR_UNKNOWN; } // sanity check
			std::wstring lastPath(lastLen, L'\0');
			if (lastLen > 0 && fread(&lastPath[0], 2, lastLen, f) != lastLen) { fclose(f); return AD_ERROR_UNKNOWN; }

			// Read imgCount (u64)
			uint64_t imgCount = 0;
			if (fread(&imgCount, 8, 1, f) != 1) { fclose(f); return AD_ERROR_UNKNOWN; }

			// Load the data file (0000.adi, etc.)
			TData data;
			data.key = key;
			data.first = firstPath;
			data.last = lastPath;
			data.size = imgCount;
			data.type = TData::Old;

			if (!LoadCollectorData(path, data, key))
			{
				fclose(f);
				return AD_ERROR_UNKNOWN;
			}
		}

		fclose(f);
		m_pStatus->Reset();
		return AD_OK;
	}

	// Load Collector-native data file (0000.adi, etc.)
	// Format: thumbSize(u32) + key(i16) + first(wstring) + last(wstring) + count(u64) + N records
	bool TImageDataStorage::LoadCollectorData(const TChar *path, TData & data, short key)
	{
		TString dataFileName = CreatePath(path, GetDataFileName(key));
		FILE* f = _wfopen(dataFileName.c_str(), L"rb");
		if (!f) return false;

		// Read thumbSize (u32)
		uint32_t fileThumbSize = 0;
		if (fread(&fileThumbSize, 4, 1, f) != 1) { fclose(f); return false; }

		// Read key (i16)
		short fileKey = 0;
		if (fread(&fileKey, 2, 1, f) != 1) { fclose(f); return false; }

		// Read first path (wstring): length(u64) + wchar_t[length*2]
		uint64_t firstLen = 0;
		if (fread(&firstLen, 8, 1, f) != 1) { fclose(f); return false; }
		if (firstLen > 10000) { fclose(f); return false; } // sanity check
		std::wstring firstPath(firstLen, L'\0');
		if (firstLen > 0 && fread(&firstPath[0], 2, firstLen, f) != firstLen) { fclose(f); return false; }

		// Read last path (wstring)
		uint64_t lastLen = 0;
		if (fread(&lastLen, 8, 1, f) != 1) { fclose(f); return false; }
		if (lastLen > 10000) { fclose(f); return false; } // sanity check
		std::wstring lastPath(lastLen, L'\0');
		if (lastLen > 0 && fread(&lastPath[0], 2, lastLen, f) != lastLen) { fclose(f); return false; }

		// Read count (u64)
		uint64_t count = 0;
		if (fread(&count, 8, 1, f) != 1) { fclose(f); return false; }

		// Read N TImageData records
		TImageData imageData(fileThumbSize);
		for (uint64_t i = 0; i < count; i++)
		{
			// Read path (wstring)
			uint64_t pathLen = 0;
			if (fread(&pathLen, 8, 1, f) != 1) { fclose(f); return false; }
			if (pathLen > 10000) { fclose(f); return false; } // sanity check
			std::wstring imgPath(pathLen, L'\0');
			if (pathLen > 0 && fread(&imgPath[0], 2, pathLen, f) != pathLen) { fclose(f); return false; }
			
			// Read metadata
			uint64_t fileSize = 0; fread(&fileSize, 8, 1, f);
			uint64_t fileTime = 0; fread(&fileTime, 8, 1, f);
			uint32_t hash = 0; fread(&hash, 4, 1, f);
			uint8_t type = 0; fread(&type, 1, 1, f);
			uint32_t width = 0; fread(&width, 4, 1, f);
			uint32_t height = 0; fread(&height, 4, 1, f);
			float blockiness = 0; fread(&blockiness, 4, 1, f);
			float blurring = 0; fread(&blurring, 4, 1, f);
			uint8_t defect = 0; fread(&defect, 1, 1, f);
			uint64_t crc32c = 0; fread(&crc32c, 8, 1, f);
			uint8_t filled = 0; fread(&filled, 1, 1, f);

			// Set image info
			imageData.path = TPath(imgPath);
			imageData.size = fileSize;
			imageData.time = fileTime;
			imageData.hash = hash;
			imageData.type = (TImageType)type;
			imageData.width = width;
			imageData.height = height;
			imageData.blockiness = blockiness;
			imageData.blurring = blurring;
			imageData.crc32c = crc32c;
			imageData.defect = (TDefectType)defect;

			// Read thumbnail data if filled
			if (filled && fileThumbSize > 0)
			{
				uint64_t thumbSizeVal = 0;
				fread(&thumbSizeVal, 8, 1, f);
				size_t thumbBytes = (size_t)thumbSizeVal;
				size_t expected = (size_t)fileThumbSize * (size_t)fileThumbSize;
				if (thumbBytes != expected || !imageData.data || imageData.data->side != (int)fileThumbSize)
				{
					fseek(f, thumbBytes, SEEK_CUR);
				}
				else
				{
					fread(imageData.data->main, 1, thumbBytes, f);
					imageData.data->filled = true;
				}
				// NvJpegCollector writes average(f32) + varianceSquare(f32) after thumb data
				fread(&imageData.data->average, 4, 1, f);
				fread(&imageData.data->varianceSquare, 4, 1, f);
			}

			if (Find(imageData) == m_storage.end())
			{
				if (IsFileExists(imageData.path.Original().c_str()))
					Insert(new TImageData(imageData));
			}
		}

	fclose(f);
		return true;
	}

	//key - номер файла индекса 0001.adi - 1
	bool TImageDataStorage::LoadData(TData & data, const TChar *path, short key)
	{
		try
		{
			TString fileName = CreatePath(path, GetDataFileName(key));
			TInputFileStream inputFile(fileName.c_str(), DATA_CONTROL_BYTES);

			inputFile.Check<TUInt32>(m_pOptions->advanced.reducedImageSize);

			inputFile.LoadChecked(data.key, key, key);
			inputFile.Load(data.first);
			inputFile.Load(data.last);
			inputFile.LoadSizeChecked(data.size, SIZE_CHECK_LIMIT);

			TImageData imageData(m_pOptions->advanced.reducedImageSize);
			for(size_t i = 0; i < data.size; i++)
			{
				inputFile.Load(imageData);
				if(Find(imageData) == m_storage.end())
				{
					if(IsFileExists(imageData.path.Original().c_str()))
						Insert(new TImageData(imageData));
				}
			}
			return true;
		}
		catch (TException e)
		{
			return e.Error == AD_OK;
		}
	}

	void TImageDataStorage::SetSaveState(const bool needToSave)
	{
		m_needToSave = needToSave;
	}
}