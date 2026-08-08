/*
* QualityDetectors - blockiness / blurring detection for NvJpegCollector.
* Port of the Simd-based detectors from AntiDupl.dll (adDataCollector / adBlurringDetector)
* so collector DB values use the same scales as the UI thresholds (blockiness 0-100, blurring radius).
*/
#ifndef __QualityDetectors_h__
#define __QualityDetectors_h__

#define SIMD_STATIC
#include "Simd/SimdLib.hpp"

#include <vector>

namespace ad
{
	typedef Simd::View<Simd::Allocator> TView;
	typedef Simd::Point<ptrdiff_t> TPoint;
	typedef uint32_t TUInt32;
	const size_t HISTOGRAM_SIZE = 256;
	const size_t BLOCKINESS_SIZE = 8;

	double GetBlockiness(const TView & gray);
	double GetBlockiness(const std::vector<unsigned int> & sums);

	class TBlurringDetector
	{
		struct TLevel
		{
			int scale;
			TView view;
			TUInt32 histogram[HISTOGRAM_SIZE];
			double quantile;
		};
		typedef std::vector<TLevel> TLevels;

	public:
		double Detect(const TView & view) const;

	private:
		void InitLevels(const TView & view, TLevels & levels) const;
		void EstimateAbsSecondDerivativeHistograms(TLevels & levels) const;
		double Quantile(const TUInt32 * histogram, double threshold) const;
		double Range(const TLevels & levels) const;
		double Threshold(double range) const;
		double Radius(const TLevels & levels, double range, double threshold) const;
	};
}

#endif//__QualityDetectors_h__
