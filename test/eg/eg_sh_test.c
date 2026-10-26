/*
 * Translate RadeonNI's drawing self-test shaders (hw/rdn_blit.c) with
 * qemu-g5's Evergreen-to-Metal translator and write the MSL out, for
 * `xcrun metal` to check.
 *   eg_sh_test <outdir>
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "radeon_juniper_sh.h"

static const uint32_t draw_vs[] = {
	0x00000004, 0x80800400, 0x0000a03c, 0x95000688, 0x00004000, 0x15200688,
	0x00000000, 0x00000000, 0x3c000000, 0x67961001, 0x00080000, 0x00000000,
	0x1c000000, 0x67961000, 0x00000008, 0x00000000,
};
static const uint32_t draw_ps[] = {
	0x00000003, 0xa00c0000, 0x00000008, 0x80400000, 0x00000000, 0x95200688,
	0x00380400, 0x00146b10, 0x00380000, 0x20146b10, 0x00380400, 0x40146b00,
	0x80380000, 0x60146b00, 0x00000000, 0x00000000, 0x00000010, 0x000d1000,
	0xb0800000, 0x00000000,
};

typedef struct { const uint32_t *w; size_t n; } Buf;
static uint32_t rdw(void *o, uint32_t i)
{
	Buf *b = o;
	return i < b->n ? b->w[i] : 0;
}

static void put(const char *dir, const char *name, const char *s)
{
	char p[512];
	FILE *f;
	snprintf(p, sizeof(p), "%s/%s", dir, name);
	f = fopen(p, "w");
	fputs(s, f);
	fclose(f);
}

int main(int argc, char **argv)
{
	Buf vb = { draw_vs, 16 }, pb = { draw_ps, 20 };
	EgProgram vs = { rdw, &vb }, ps = { rdw, &pb };
	EgVsKey vk;
	EgPsKey pk;
	EgUsage vu, pu;
	char *err = NULL, *src, *d;
	const char *dir = argc > 1 ? argv[1] : ".";

	d = eg_disasm(&vs); printf("VS:\n%s", d); free(d);
	d = eg_disasm(&ps); printf("PS:\n%s", d); free(d);

	memset(&vk, 0, sizeof(vk));
	vk.tex_dim[0] = 1;
	src = eg_translate_vs(&vs, NULL, &vk, &vu, &err);
	printf("VS: params 0x%x vtx 0x%x unsupported %d %s\n", vu.params, vu.vtx_used,
	       vu.unsupported, err ? err : "");
	put(dir, "vs.metal", src);
	free(src);

	memset(&pk, 0, sizeof(pk));
	pk.ninput = 1;
	pk.input_cntl[0] = 0;
	pk.vs_param_of_input[0] = 0;
	pk.tex_dim[0] = 1;
	pk.target[0] = (EgTarget){ 0x1a, 0, 0, 2, 0xf, false, 0 };
	err = NULL;
	src = eg_translate_ps(&ps, &pk, &pu, &err);
	printf("PS: tex 0x%x samp 0x%x unsupported %d %s\n", pu.tex_used, pu.samp_used,
	       pu.unsupported, err ? err : "");
	put(dir, "ps.metal", src);
	free(src);

	/* depth and stencil variants */
	{
		static const struct { uint8_t zf; bool st; uint32_t dc, rc; const char *name; } v[] = {
			{ 2, true, 0x0000001e | (2u << 8) | (1u << 11) | (2u << 14) | (3u << 17) | 0x80 | (7u << 20), 0, "ps_z24s8.metal" },
			{ 1, false, 0x00000016, 0, "ps_z16.metal" },
			{ 3, false, 0x00000066, 0, "ps_z32f.metal" },
			{ 2, true, 0, 3, "ps_clear.metal" },
			{ 2, true, 0, 0xc, "ps_copy.metal" },
		};
		for (unsigned i = 0; i < sizeof(v) / sizeof(v[0]); i++) {
			pk.z_format = v[i].zf;
			pk.stencil = v[i].st;
			pk.db_depth_control = v[i].dc;
			pk.db_render_control = v[i].rc;
			err = NULL;
			src = eg_translate_ps(&ps, &pk, &pu, &err);
			put(dir, v[i].name, src);
			free(src);
		}
	}
	return 0;
}
