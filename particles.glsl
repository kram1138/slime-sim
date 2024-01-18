#[compute]
#version 450

const int NUM_PARTICLES = 1024;

// Invocations in the (x, y, z) dimension
layout(local_size_x = 16, local_size_y = 1, local_size_z = 1) in;

struct Particle {
	vec2 position;
	vec2 velocity;
};

// A binding to the buffer we create in our script
layout(set = 0, binding = 0, std430) restrict buffer Particles {
	Particle data[NUM_PARTICLES];
}
particles;

layout(set = 0, binding = 1, std430) restrict buffer Params{
    float delta;
    float speed;
	float turningRate;
	float sampleRadius;
	float sampleDistance;
	float sampleAngle;
} params;

layout(set = 0, binding = 2, rgba32f) uniform image2D INPUT_TEXTURE;

vec4 getPixel(ivec2 texel) {
	return imageLoad(INPUT_TEXTURE, texel);
}

float random (vec2 st) {
    return fract(sin(dot(st.xy, vec2(12.9898,78.233)))* 43758.5453123);
}

vec2 rotate(vec2 v, float a) {
	float s = sin(a);
	float c = cos(a);
	mat2 m = mat2(c, -s, s, c);
	return m * v;
}

vec4 sampleArea(ivec2 texel, int radius, ivec2 resolution) {
	vec4 sum = vec4(0.0);
	for (int x = -radius; x <= radius; x++) {
		for (int y = -radius; y <= radius; y++) {
			vec4 sampleTexel = clamp(getPixel(texel + ivec2(x, y)), resolution.x, resolution.y);
			sum += getPixel(texel + ivec2(x, y));
		}
	}
	return sum / float((radius * 2 + 1) * (radius * 2 + 1));
}

// The code we want to execute in each invocation
void main() {
	vec2 position = particles.data[gl_GlobalInvocationID.x].position;
	vec2 velocity = particles.data[gl_GlobalInvocationID.x].velocity;

    position.x += velocity.x * params.speed * params.delta;
	position.y += velocity.y * params.speed * params.delta;

	if (position.x > 1.0) {
		float newAngle = random(position) * 3.1415926 + 1.57079;
		velocity.x = cos(newAngle);
		velocity.y = sin(newAngle);
	} else if (position.x < 0.0) {
		float newAngle = random(position) * 3.1415926 - 1.57079;
		velocity.x = cos(newAngle);
		velocity.y = sin(newAngle);
	}
	if (position.y > 1.0) {
		float newAngle = -random(position) * 3.1415926;
		velocity.x = cos(newAngle);
		velocity.y = sin(newAngle);
	} else if (position.y < 0.0) {
		float newAngle = random(position) * 3.1415926;
		velocity.x = cos(newAngle);
		velocity.y = sin(newAngle);
	} else {
		ivec2 resolution = imageSize(INPUT_TEXTURE);
		ivec2 texel = ivec2(position * vec2(resolution));

		ivec2 offsetLeft = ivec2(params.sampleDistance * cos(params.sampleAngle), params.sampleDistance * sin(params.sampleAngle));
		ivec2 offsetStraight = ivec2(params.sampleDistance);
		ivec2 offsetRight = ivec2(params.sampleDistance * cos(-params.sampleAngle), params.sampleDistance * sin(-params.sampleAngle));
		
		vec4 valLeft = sampleArea(texel + offsetLeft, int(params.sampleRadius), resolution);
		vec4 valStraight = sampleArea(texel + offsetStraight, int(params.sampleRadius), resolution);
		vec4 valRight = sampleArea(texel + offsetRight, int(params.sampleRadius), resolution);

		float left = valLeft.r;
		float straight = valStraight.r;
		float right = valRight.r;

		if (left > straight && left > right) {
			velocity = rotate(velocity, params.turningRate * params.delta);
		} else if (right > straight && right > left) {
			velocity = rotate(velocity, -params.turningRate * params.delta);
		}
	}

	position = clamp(position, 0.0, 1.0);

	particles.data[gl_GlobalInvocationID.x].position = position;
	particles.data[gl_GlobalInvocationID.x].velocity = velocity;
}