/* RadeonNI's compute_pll() (radeon_compute_pll_avivo), run over a grid of
 * clocks and flags; test/pll/pll.fs prints the same from the Forth port. */
#include <stdio.h>
#include <stdint.h>
#include <stdbool.h>
#include <string.h>
#include "compute_pll.inc"

int main(void)
{
	static const uint32_t flagsets[] = {
		RADEON_PLL_PREFER_LOW_REF_DIV,
		RADEON_PLL_PREFER_LOW_REF_DIV | RADEON_PLL_USE_FRAC_FB_DIV,
		RADEON_PLL_USE_FRAC_FB_DIV | RADEON_PLL_USE_REF_DIV,
		RADEON_PLL_USE_FRAC_FB_DIV | RADEON_PLL_USE_POST_DIV,
		RADEON_PLL_USE_FRAC_FB_DIV | RADEON_PLL_USE_REF_DIV | RADEON_PLL_USE_POST_DIV,
	};
	struct rdn_pll pll;
	unsigned f, clk;

	for (f = 0; f < 5; f++)
		for (clk = 20000; clk <= 340000; clk += 137) {
			uint32_t fb, frac, ref, post;

			memset(&pll, 0, sizeof(pll));
			pll.reference_freq = 2700;
			pll.pll_out_min = 60000;
			pll.pll_out_max = 120000;
			pll.min_post_div = 2; pll.max_post_div = 0x7f;
			pll.min_ref_div = 2; pll.max_ref_div = 0x3ff;
			pll.min_feedback_div = 4; pll.max_feedback_div = 0x7ff;
			pll.flags = flagsets[f];
			pll.reference_div = 2 + clk % 7;
			pll.post_div = 4 + clk % 5;
			compute_pll(&pll, clk, &fb, &frac, &ref, &post);
			printf("%u %u: %u %u %u %u\n", f, clk, fb, frac, ref, post);
		}
	return 0;
}
