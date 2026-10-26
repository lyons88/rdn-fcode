#define PS_WORDS		16

/* PLL flags (radeon_mode.h) */
#define RADEON_PLL_USE_REF_DIV		(1 << 2)
#define RADEON_PLL_PREFER_LOW_REF_DIV	(1 << 6)
#define RADEON_PLL_USE_FRAC_FB_DIV	(1 << 10)
#define RADEON_PLL_USE_POST_DIV		(1 << 12)

struct rdn_pll {
	uint32_t reference_freq;	/* 10 kHz units */
	uint32_t reference_div, post_div;
	uint32_t pll_out_min, pll_out_max;
	uint32_t min_ref_div, max_ref_div;
	uint32_t min_post_div, max_post_div;
	uint32_t min_feedback_div, max_feedback_div;
	uint32_t flags;
};
static unsigned gcd_u(unsigned a, unsigned b)
{
	while (b) {
		unsigned t = a % b;

		a = b;
		b = t;
	}
	return a;
}

#define DIV_ROUND_UP(n, d)	(((n) + (d) - 1) / (d))
#define DIV_ROUND_CLOSEST(n, d)	(((n) + (d) / 2) / (d))

static unsigned clamp_u(unsigned v, unsigned lo, unsigned hi)
{
	return v < lo ? lo : (v > hi ? hi : v);
}

static void avivo_reduce_ratio(unsigned *nom, unsigned *den,
			       unsigned nom_min, unsigned den_min)
{
	unsigned tmp;

	/* reduce the numbers to a simpler ratio */
	tmp = gcd_u(*nom, *den);
	*nom /= tmp;
	*den /= tmp;

	/* make sure nominator is large enough */
	if (*nom < nom_min) {
		tmp = DIV_ROUND_UP(nom_min, *nom);
		*nom *= tmp;
		*den *= tmp;
	}

	/* make sure the denominator is large enough */
	if (*den < den_min) {
		tmp = DIV_ROUND_UP(den_min, *den);
		*nom *= tmp;
		*den *= tmp;
	}
}

static void avivo_get_fb_ref_div(unsigned nom, unsigned den, unsigned post_div,
				 unsigned fb_div_max, unsigned ref_div_max,
				 unsigned *fb_div, unsigned *ref_div)
{
	/* limit reference * post divider to a maximum */
	ref_div_max = clamp_u(100 / post_div, 1u, ref_div_max);

	/* get matching reference and feedback divider */
	*ref_div = clamp_u(den / post_div, 1u, ref_div_max);
	*fb_div = DIV_ROUND_CLOSEST(nom * *ref_div * post_div, den);

	/* limit fb divider to its maximum */
	if (*fb_div > fb_div_max) {
		*ref_div = (*ref_div * fb_div_max) / (*fb_div);
		*fb_div = fb_div_max;
	}
}

/* radeon_compute_pll_avivo(); `freq` is in kHz */
static void compute_pll(const struct rdn_pll *pll, uint32_t freq,
			uint32_t *fb_div_p, uint32_t *frac_fb_div_p,
			uint32_t *ref_div_p, uint32_t *post_div_p)
{
	unsigned target_clock = pll->flags & RADEON_PLL_USE_FRAC_FB_DIV ?
		freq : freq / 10;
	unsigned fb_div_min, fb_div_max, fb_div;
	unsigned post_div_min, post_div_max, post_div;
	unsigned ref_div_min, ref_div_max, ref_div;
	unsigned post_div_best, diff_best;
	unsigned nom, den;

	/* determine allowed feedback divider range */
	fb_div_min = pll->min_feedback_div;
	fb_div_max = pll->max_feedback_div;

	if (pll->flags & RADEON_PLL_USE_FRAC_FB_DIV) {
		fb_div_min *= 10;
		fb_div_max *= 10;
	}

	/* determine allowed ref divider range */
	if (pll->flags & RADEON_PLL_USE_REF_DIV)
		ref_div_min = pll->reference_div;
	else
		ref_div_min = pll->min_ref_div;

	if (pll->flags & RADEON_PLL_USE_FRAC_FB_DIV &&
	    pll->flags & RADEON_PLL_USE_REF_DIV)
		ref_div_max = pll->reference_div;
	else
		ref_div_max = pll->max_ref_div;

	/* determine allowed post divider range */
	if (pll->flags & RADEON_PLL_USE_POST_DIV) {
		post_div_min = pll->post_div;
		post_div_max = pll->post_div;
	} else {
		unsigned vco_min, vco_max;

		vco_min = pll->pll_out_min;
		vco_max = pll->pll_out_max;

		if (pll->flags & RADEON_PLL_USE_FRAC_FB_DIV) {
			vco_min *= 10;
			vco_max *= 10;
		}

		post_div_min = vco_min / target_clock;
		if ((target_clock * post_div_min) < vco_min)
			++post_div_min;
		if (post_div_min < pll->min_post_div)
			post_div_min = pll->min_post_div;

		post_div_max = vco_max / target_clock;
		if ((target_clock * post_div_max) > vco_max)
			--post_div_max;
		if (post_div_max > pll->max_post_div)
			post_div_max = pll->max_post_div;
	}

	/* represent the searched ratio as fractional number */
	nom = target_clock;
	den = pll->reference_freq;

	/* reduce the numbers to a simpler ratio */
	avivo_reduce_ratio(&nom, &den, fb_div_min, post_div_min);

	/* now search for a post divider */
	post_div_best = post_div_max;
	diff_best = ~0u;

	for (post_div = post_div_min; post_div <= post_div_max; ++post_div) {
		unsigned diff, got;

		avivo_get_fb_ref_div(nom, den, post_div, fb_div_max,
				     ref_div_max, &fb_div, &ref_div);
		got = (pll->reference_freq * fb_div) / (ref_div * post_div);
		diff = target_clock > got ? target_clock - got : got - target_clock;

		if (diff <= diff_best) {
			post_div_best = post_div;
			diff_best = diff;
		}
	}
	post_div = post_div_best;

	/* get the feedback and reference divider for the optimal value */
	avivo_get_fb_ref_div(nom, den, post_div, fb_div_max, ref_div_max,
			     &fb_div, &ref_div);

	/* reduce the numbers to a simpler ratio once more */
	/* this also makes sure that the reference divider is large enough */
	avivo_reduce_ratio(&fb_div, &ref_div, fb_div_min, ref_div_min);

	/* avoid high jitter with small fractional dividers */
	if (pll->flags & RADEON_PLL_USE_FRAC_FB_DIV && (fb_div % 10)) {
		unsigned jitter_min = (9 - (fb_div % 10)) * 20 + 50;

		if (fb_div_min < jitter_min)
			fb_div_min = jitter_min;
		if (fb_div < fb_div_min) {
			unsigned tmp = DIV_ROUND_UP(fb_div_min, fb_div);

			fb_div *= tmp;
			ref_div *= tmp;
		}
	}

	/* and finally save the result */
	if (pll->flags & RADEON_PLL_USE_FRAC_FB_DIV) {
		*fb_div_p = fb_div / 10;
		*frac_fb_div_p = fb_div % 10;
	} else {
		*fb_div_p = fb_div;
		*frac_fb_div_p = 0;
	}
	*ref_div_p = ref_div;
	*post_div_p = post_div;
}
