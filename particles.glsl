#[compute]
#version 450

const int NUM_PARTICLES = 262144;

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

layout(set = 0, binding = 2, rgba32f) uniform image2D TEXTURE;

vec4 getPixel(ivec2 texel) {
	return imageLoad(TEXTURE, texel);
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

ivec2 move(ivec2 origin, float dist, float angle) {
	return origin + ivec2(dist * cos(angle), dist * sin(angle));
}
const float PI = 3.1415926;
const float PI2 = 1.5707963;
const float PI4 = 0.7853981;
// The code we want to execute in each invocation
void main() {
	vec2 position = particles.data[gl_GlobalInvocationID.x].position;
	vec2 velocity = particles.data[gl_GlobalInvocationID.x].velocity;

    position.x += velocity.x * params.speed * params.delta;
	position.y += velocity.y * params.speed * params.delta;

	
	if (position.x > 1.0) {
		float newAngle = random(position) * PI2 + 3 * PI4;
		velocity.x = cos(newAngle);
		velocity.y = sin(newAngle);
	} else if (position.x < 0.0) {
		float newAngle = random(position) * PI2 - PI4;
		velocity.x = cos(newAngle);
		velocity.y = sin(newAngle);
	}
	if (position.y > 1.0) {
		float newAngle = random(position) * PI2 - 3 * PI4;
		velocity.x = cos(newAngle);
		velocity.y = sin(newAngle);
	} else if (position.y < 0.0) {
		float newAngle = random(position) * PI2 + PI4;
		velocity.x = cos(newAngle);
		velocity.y = sin(newAngle);
	} else {
		ivec2 resolution = imageSize(TEXTURE);
		ivec2 texel = ivec2(position * vec2(resolution));
		float sampleAngleRads = params.sampleAngle * 3.1415926 / 180.0;
		float particleAngle = atan(velocity.y, velocity.x);

		float randomStrength = random(position);

		ivec2 offsetLeft = ivec2(params.sampleDistance * cos(particleAngle + sampleAngleRads), params.sampleDistance * sin(particleAngle + sampleAngleRads));
		ivec2 offsetStraight = ivec2(params.sampleDistance * cos(particleAngle), int(params.sampleDistance * sin(particleAngle)));
		ivec2 offsetRight = ivec2(params.sampleDistance * cos(particleAngle - sampleAngleRads), params.sampleDistance * sin(particleAngle - sampleAngleRads));

		ivec2 leftPoint = texel + offsetLeft;
		ivec2 straightPoint = texel + offsetStraight;
		ivec2 rightPoint = texel + offsetRight;
		
		vec4 valLeft = sampleArea(texel + offsetLeft, int(params.sampleRadius), resolution);
		vec4 valStraight = sampleArea(texel + offsetStraight, int(params.sampleRadius), resolution);
		vec4 valRight = sampleArea(texel + offsetRight, int(params.sampleRadius), resolution);

		float left = valLeft.r;
		float straight = valStraight.r;
		float right = valRight.r;

		if (left > straight && left > right) {
			velocity = rotate(velocity, -params.turningRate * randomStrength * params.delta);
		} else if (right > straight && right > left) {
			velocity = rotate(velocity, params.turningRate * randomStrength * params.delta);
		} else {
			velocity = rotate(velocity, (randomStrength * 2 - 1) * params.turningRate * params.delta);
		}
	}

	position = clamp(position, 0.0, 1.0);

	particles.data[gl_GlobalInvocationID.x].position = position;
	particles.data[gl_GlobalInvocationID.x].velocity = velocity;

	ivec2 texel = ivec2(position * vec2(imageSize(TEXTURE)));
	vec4 color = getPixel(texel);
	color = clamp(color + vec4(0.05, 0.05, 0.05, 0.0), 0.0, 1.0);
	imageStore(TEXTURE, texel, color);
}