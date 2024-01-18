#[compute]
#version 450

// Invocations in the (x, y, z) dimension
layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

struct Particle {
	vec2 position;
	vec2 velocity;
};

// A binding to the buffer we create in our script
layout(set = 0, binding = 0, std430) restrict buffer Particles {
	Particle data[100];
}
particles;

layout(set = 0, binding = 1, std430) restrict buffer Params{
    float delta;
    float speed;
} params;


float random (vec2 st) {
    return fract(sin(dot(st.xy, vec2(12.9898,78.233)))* 43758.5453123);
}


// The code we want to execute in each invocation
void main() {
	vec2 position = particles.data[gl_GlobalInvocationID.x].position;
	vec2 velocity = particles.data[gl_GlobalInvocationID.x].velocity;

    position.x += velocity.x * params.speed * params.delta;
	position.y += velocity.y * params.speed * params.delta;

	float speed = length(velocity);
	if (position.x > 1.0) {
		float newAngle = random(position) * 3.1415926 + 1.57079;
		velocity.x = speed * cos(newAngle);
		velocity.y = speed * sin(newAngle);
	} else if (position.x < 0.0) {
		float newAngle = random(position) * 3.1415926 - 1.57079;
		velocity.x = speed * cos(newAngle);
		velocity.y = speed * sin(newAngle);
	}
	if (position.y > 1.0) {
		float newAngle = -random(position) * 3.1415926;
		velocity.x = speed * cos(newAngle);
		velocity.y = speed * sin(newAngle);
	} else if (position.y < 0.0) {
		float newAngle = random(position) * 3.1415926;
		velocity.x = speed * cos(newAngle);
		velocity.y = speed * sin(newAngle);
	}

	position = clamp(position, 0.0, 1.0);

	particles.data[gl_GlobalInvocationID.x].position = position;
	particles.data[gl_GlobalInvocationID.x].velocity = velocity;
}