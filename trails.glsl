#[compute]
#version 450
#define pow2(x) (x * x)
// Invocations in the (x, y, z) dimension
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

struct Particle {
	vec2 position;
	vec2 velocity;
};
const int NUM_PARTICLES = 1024;

// A binding to the buffer we create in our script
layout(set = 0, binding = 0, std430) restrict buffer Particles {
	Particle data[NUM_PARTICLES];
}
particles;
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
	float closest = 1.0;
	for (int i = 0; i < NUM_PARTICLES; i++) {
		vec2 position = particles.data[i].position;
		if (abs(distance(position, uv)) < closest) {
			closest = abs(distance(position, uv));
		}
	}

	if (closest < params.particleSize) {
		color = vec4(1.0);
	} else {
		color.xyz -= params.delta * params.decayRate;
	}

	imageStore(OUTPUT_TEXTURE, texel, color);
}