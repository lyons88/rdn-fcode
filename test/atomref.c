/*
 * Reference run of Linux's AtomBIOS interpreter (RadeonNI's copy of
 * drivers/gpu/drm/radeon/atom.c) against a fake register file.
 *
 *   atomref <vbios.rom> <calls.txt>
 *
 * calls.txt has one command table call per line:
 *   asic                      atom_asic_init()
 *   call <index> <16 dwords>  atom_execute_table() with that parameter block
 *
 * Every register access, delay and the result of each call goes to stdout.
 * test/replay.fs feeds the same reads back to the Forth interpreter and
 * checks it makes the same writes, so the two can be compared exactly.
 *
 * The register file returns what was last written. A register read 64
 * times in a row without a write in between comes back inverted once, so
 * polling loops end in both runs the same way.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>
#include "atom.h"

#define NREGS (1u << 20)
static uint32_t regs[NREGS];
static uint32_t last_idx = ~0u, repeat;
static uint64_t fake_ms;

/*
 * Status bits the hardware sets on its own, which AtomBIOS waits for (the
 * same list is in test/prelude.fs and the QEMU model): memory training done
 * after the MC sequencer starts.
 */
static uint32_t ready_bits(uint32_t idx)
{
	switch (idx) {
	case 0x0a88: return 1u << 17;	/* 0x2a20: MC_SEQ training done */
	case 0x0a74: return 1u << 8;	/* 0x29d0 MC_IO_PAD_CNTL_D0: MEM_FALL_OUT_CMD */
	}
	return 0;
}

static uint32_t model_read(uint32_t idx)
{
	uint32_t v;

	idx &= NREGS - 1;
	if (idx == last_idx)
		repeat++;
	else
		repeat = 0;
	last_idx = idx;
	v = regs[idx] | ready_bits(idx);
	if (repeat >= 64) {
		repeat = 0;
		v = ~v;
	}
	return v;
}

static void model_write(uint32_t idx, uint32_t v)
{
	regs[idx & (NREGS - 1)] = v;
	last_idx = ~0u;
}

static uint32_t c_reg_read(struct card_info *c, uint32_t idx)
{
	uint32_t v = model_read(idx);

	printf("R %x %x\n", idx, v);
	return v;
}
static void c_reg_write(struct card_info *c, uint32_t idx, uint32_t v)
{
	printf("W %x %x\n", idx, v);
	model_write(idx, v);
}
static uint32_t c_io_read(struct card_info *c, uint32_t idx)
{
	uint32_t v = model_read(idx);

	printf("R %x %x\n", idx, v);
	return v;
}
static void c_io_write(struct card_info *c, uint32_t idx, uint32_t v)
{
	printf("W %x %x\n", idx, v);
	model_write(idx, v);
}
static uint32_t c_pll_read(struct card_info *c, uint32_t idx)
{
	printf("P %x\n", idx);
	return 0;
}
static void c_pll_write(struct card_info *c, uint32_t idx, uint32_t v)
{
	printf("P %x\n", idx);
}
static uint32_t c_mc_read(struct card_info *c, uint32_t idx)
{
	printf("M %x\n", idx);
	return 0;
}
static void c_mc_write(struct card_info *c, uint32_t idx, uint32_t v)
{
	printf("M %x\n", idx);
}

static void os_delay(void *k, uint32_t us) { printf("D %x\n", us); }
static uint64_t os_time(void *k) { return fake_ms++; }
static void *os_alloc(void *k, size_t n) { return calloc(1, n); }
static void os_free(void *k, void *p) { free(p); }
static void os_log(void *k, enum rdn_log_level l, const char *fmt, va_list ap)
{
	fputs("# ", stdout);
	vprintf(fmt, ap);
	fputs("\n", stdout);
}

int main(int argc, char **argv)
{
	static uint8_t bios[0x20000];
	struct rdn_os os = { 0 };
	struct card_info card = { 0 };
	struct atom_context *ctx;
	char line[1024];
	FILE *f;

	if (argc != 3) {
		fprintf(stderr, "usage: atomref vbios.rom calls.txt\n");
		return 2;
	}
	f = fopen(argv[1], "rb");
	if (!f || fread(bios, 1, sizeof(bios), f) < 0x10000) {
		perror(argv[1]);
		return 1;
	}
	fclose(f);

	os.delay_us = os_delay;
	os.time_ms = os_time;
	os.alloc = os_alloc;
	os.free = os_free;
	os.log = os_log;
	card.os = &os;
	card.reg_read = c_reg_read;
	card.reg_write = c_reg_write;
	card.ioreg_read = c_io_read;
	card.ioreg_write = c_io_write;
	card.pll_read = c_pll_read;
	card.pll_write = c_pll_write;
	card.mc_read = c_mc_read;
	card.mc_write = c_mc_write;

	ctx = atom_parse(&card, bios);
	if (!ctx || atom_allocate_fb_scratch(ctx))
		return 1;

	f = fopen(argv[2], "r");
	if (!f) {
		perror(argv[2]);
		return 1;
	}
	while (fgets(line, sizeof(line), f)) {
		uint32_t ps[16] = { 0 };
		unsigned idx;
		int r, i, n;
		char *p = line;

		if (!strncmp(line, "asic", 4)) {
			printf("CALL asic\n");
			r = atom_asic_init(ctx);
			printf("END %x\n", r ? 1 : 0);
			continue;
		}
		if (sscanf(p, "call %x%n", &idx, &n) != 1)
			continue;
		p += n;
		for (i = 0; i < 16; i++) {
			unsigned v;

			if (sscanf(p, "%x%n", &v, &n) != 1)
				break;
			ps[i] = v;	/* the host is little-endian, like the block */
			p += n;
		}
		printf("CALL %x\n", idx);
		r = atom_execute_table(ctx, idx, ps, 16);
		printf("END %x", r ? 1 : 0);
		for (i = 0; i < 16; i++)
			printf(" %x", ps[i]);
		printf("\n");
	}
	return 0;
}
