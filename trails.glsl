#[compute]
#version 450
#define pow2(x) (x * x)
// Invocations in the (x, y, z) dimension
layout(local_size_x = 32, local_size_y = 32, local_size_z = 1) in;

struct Particle {
	vec2 position;
	vec2 velocity;
};

// A binding to the buffer we create in our script
layout(set = 0, binding = 0, std430) restrict buffer Particles {
	Particle data[100];
}
particles;
layout(set = 0, binding = 1, rgba32f) uniform image2D INPUT_TEXTURE;
layout(set = 0, binding = 2, rgba32f) uniform image2D OUTPUT_TEXTURE;

layout(set = 0, binding = 3, std430) restrict buffer Params{
	float delta;
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


const float pi = atan(1.0) * 4.0;
const int samples = 35;
const float sigma = float(samples) * 0.25;
float gaussian(vec2 i) {
    return 1.0 / (2.0 * pi * pow2(sigma)) * exp(-((pow2(i.x) + pow2(i.y)) / (2.0 * pow2(sigma))));
}

vec3 blur(vec2 uv, vec2 scale) {
    vec3 col = vec3(0.0);
    float accum = 0.0;
    float weight;
    vec2 offset;
    
    for (int x = -samples / 2; x < samples / 2; ++x) {
        for (int y = -samples / 2; y < samples / 2; ++y) {
            offset = vec2(x, y);
            weight = gaussian(offset);
            col += getPixel(uv + scale * offset).rgb * weight;
            accum += weight;
        }
    }
    
    return col / accum;
}

void main() {
	ivec2 resolution = imageSize(INPUT_TEXTURE);
	ivec2 texel = ivec2(gl_GlobalInvocationID.xy);
	vec2 uv = vec2(texel) / vec2(resolution);
	vec4 color = getPixel(texel);
	float closest = 1.0;
	for (int i = 0; i < 100; i++) {
		vec2 position = particles.data[i].position;
		if (abs(distance(position, uv)) < closest) {
			closest = abs(distance(position, uv));
		}
	}

	if (closest < params.particleSize) {
		color = vec4(1.0);
	}

	imageStore(OUTPUT_TEXTURE, texel, color);
}