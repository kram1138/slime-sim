#[compute]
#version 450
// Invocations in the (x, y, z) dimension
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(set = 0, binding = 0, rgba32f) uniform image2D INPUT_TEXTURE;
layout(set = 0, binding = 1, rgba32f) uniform image2D OUTPUT_TEXTURE;

layout(set = 0, binding = 2, std430) restrict buffer Params{
	float delta;
	float diffuseRate;
} params;

vec4 getPixel(ivec2 texel) {
	return imageLoad(INPUT_TEXTURE, texel);
}

void main() {
	ivec2 id = ivec2(gl_GlobalInvocationID.xy);
	ivec2 resolution = imageSize(INPUT_TEXTURE);
	int width = resolution.x;
	int height = resolution.y;
	
	if (id.x < 0 || id.x >= uint(width) || id.y < 0 || id.y >= uint(height)) {
		return;
	}

	vec4 sum = vec4(0);
	vec4 originalCol = getPixel(id);
	// 3x3 blur
	for (int offsetX = -1; offsetX <= 1; offsetX ++) {
		for (int offsetY = -1; offsetY <= 1; offsetY ++) {
			int sampleX = min(width-1, max(0, id.x + offsetX));
			int sampleY = min(height-1, max(0, id.y + offsetY));
			sum += getPixel(ivec2(sampleX, sampleY));
		}
	}

	vec4 blurredCol = sum / 9;
	float diffuseWeight = clamp(params.diffuseRate * params.delta, 0.0, 1.0);
	blurredCol = originalCol * (1 - diffuseWeight) + blurredCol * (diffuseWeight);

	imageStore(OUTPUT_TEXTURE, id, max(vec4(0.0), blurredCol));
}