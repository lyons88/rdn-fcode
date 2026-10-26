/* The MSL eg_tile() (in every generated pixel shader) must agree with eg_tile_offset(). */
#import <Metal/Metal.h>
#include <stdio.h>
#include <string.h>
#include "radeon_juniper_sh.h"

static uint32_t rdw(void *o, uint32_t i) { static const uint32_t ps[] = { 0x00000000, 0x95200688 }; return i < 2 ? ps[i] : 0; }

int main(void)
{
	@autoreleasepool {
		id<MTLDevice> dev = MTLCreateSystemDefaultDevice();
		EgProgram p = { rdw, NULL };
		EgPsKey k; EgUsage u; char *err = NULL;
		memset(&k, 0, sizeof(k));
		char *msl = eg_translate_ps(&p, &k, &u, &err);
		NSString *src = [NSString stringWithFormat:@"%s\nkernel void tk(device uint *out [[buffer(0)]], constant uint4 &t [[buffer(1)]], constant uint &sl [[buffer(2)]], uint2 g [[thread_position_in_grid]]) { out[g.y * t.y + g.x] = eg_tile(t, g.x, g.y, sl); }\n", msl];
		NSError *e = nil;
		id<MTLLibrary> lib = [dev newLibraryWithSource:src options:nil error:&e];
		if (!lib) { printf("compile: %s\n", [[e description] UTF8String]); return 1; }
		id<MTLComputePipelineState> ps = [dev newComputePipelineStateWithFunction:[lib newFunctionWithName:@"tk"] error:&e];
		id<MTLCommandQueue> q = [dev newCommandQueue];
		unsigned bpps[] = { 1, 2, 4, 8, 16 }, modes[] = { 1, 2, 4 }, fails = 0, cases = 0;
		for (unsigned mi = 0; mi < 3; mi++) for (unsigned bi = 0; bi < 5; bi++) for (unsigned nd = 0; nd < 2; nd++) for (unsigned sl = 0; sl < 2; sl++) {
			EgTile t = { modes[mi], bpps[bi], 0, 0, 4, 8, 1, 2, 1, 512, 256, nd };
			t.pitch = 8 * t.bank_w * t.pipes * t.aspect * 3; t.height = 8 * t.bank_h * t.banks / t.aspect * 2;
			uint32_t tp[4]; eg_tile_pack(&t, tp);
			id<MTLBuffer> out = [dev newBufferWithLength:t.pitch * t.height * 4 options:MTLResourceStorageModeShared];
			id<MTLCommandBuffer> cb = [q commandBuffer];
			id<MTLComputeCommandEncoder> ce = [cb computeCommandEncoder];
			[ce setComputePipelineState:ps];
			[ce setBuffer:out offset:0 atIndex:0];
			[ce setBytes:tp length:16 atIndex:1];
			[ce setBytes:&sl length:4 atIndex:2];
			[ce dispatchThreads:MTLSizeMake(t.pitch, t.height, 1) threadsPerThreadgroup:MTLSizeMake(8, 8, 1)];
			[ce endEncoding]; [cb commit]; [cb waitUntilCompleted];
			const uint32_t *o = out.contents;
			for (uint32_t y = 0; y < t.height; y++) for (uint32_t x = 0; x < t.pitch; x++) {
				uint64_t c = eg_tile_offset(&t, x, y, sl);
				if (o[y * t.pitch + x] != (uint32_t)c) { if (fails < 5) printf("mode %u bpp %u nd %u (%u,%u): msl %u c %llu\n", t.mode, t.bpp, nd, x, y, o[y * t.pitch + x], c); fails++; }
			}
			cases++;
		}
		printf("%u cases, %s\n", cases, fails ? "MISMATCH" : "PASS");
		return fails != 0;
	}
}
