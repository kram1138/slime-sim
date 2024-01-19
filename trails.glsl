#[compute]
#version 450
#define pow2(x) (x * x)
// Invocations in the (x, y, z) dimension
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(set = 0, binding = 1, rgba32f) uniform image2D INPUT_TEXTURE;
layout(set = 0, binding = 2, rgba32f) uniform image2D OUTPUT_TEXTURE;

layout(set = 0, binding = 3, std430) restrict buffer Params{
	float delta;
	float decayRate;
	float particleSize;
} params;

vec4 getPixel(ivec2 texel) {
	return imageLoad(INPUT_TEXTURE, texel);
}

vec4 getPixel(vec2 uv) {
	ivec2 resolution = imageSize(INPUT_TEXTURE);
	ivec2 texel = ivec2(uv * vec2(resolution));
	return getPixel(texel);
}

void main() {
	ivec2 resolution = imageSize(INPUT_TEXTURE);
	ivec2 texel = ivec2(gl_GlobalInvocationID.xy);
	vec2 uv = vec2(texel) / vec2(resolution);
	vec4 color = getPixel(texel);
	color.xyz -= params.delta * params.decayRate;
	imageStore(OUTPUT_TEXTURE, texel, color);
}