/* eg_tile_offset must map every element of a surface to its own address inside it. */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "radeon_juniper_sh.h"

static int check(EgTile t, int layers)
{
	uint64_t size = (uint64_t)t.pitch * t.height * t.bpp * layers, n = 0;
	uint8_t *seen = calloc(size, 1);
	uint32_t x, y, l, b;

	for (l = 0; l < (uint32_t)layers; l++)
		for (y = 0; y < t.height; y++)
			for (x = 0; x < t.pitch; x++) {
				uint64_t o = eg_tile_offset(&t, x, y, l);
				if (o % t.bpp || o + t.bpp > size) { printf("out of range\n"); return 1; }
				for (b = 0; b < t.bpp; b++) {
					if (seen[o + b]++) { printf("collision mode %u bpp %u at %u,%u\n", t.mode, t.bpp, x, y); return 1; }
				}
				n++;
			}
	free(seen);
	return 0;
}

int main(void)
{
	int fails = 0;
	unsigned bpp[] = { 1, 2, 4, 8, 16 }, i, mode, banks, bw, bh, asp, split;

	for (mode = 1; mode <= 4; mode++) {
		if (mode == 3) continue;
		for (i = 0; i < 5; i++)
		for (banks = 4; banks <= 8; banks *= 2)
		for (bw = 1; bw <= 2; bw *= 2)
		for (bh = 1; bh <= 2; bh *= 2)
		for (asp = 1; asp <= 2; asp *= 2)
		for (split = 256; split <= 1024; split *= 4) {
			EgTile t = { mode, bpp[i], 0, 0, 4, banks, bw, bh, asp, split, 256, i & 1 };
			uint32_t mtp = 8 * bw * 4 * asp, mth = 8 * bh * banks / asp;
			t.pitch = mode == 4 ? mtp * 3 : 64;
			t.height = mode == 4 ? mth * 2 : 48;
			fails += check(t, 2);
		}
	}
	printf(fails ? "FAIL %d\n" : "PASS\n", fails);
	return fails != 0;
}
