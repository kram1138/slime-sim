shader_type canvas_item;

uniform vec2 position;
uniform float angle;
uniform float sampleRadius;
uniform float sampleDistance;
uniform float sampleAngle;

vec2 rotate(vec2 v, float a) {
	float s = sin(a);
	float c = cos(a);
	mat2 m = mat2(vec2(c, -s), vec2(s, c));
	return m * v;
}

void vertex() {
}

void fragment() {
	float sampleAngleRads = sampleAngle * PI / 180.0;
	ivec2 offsetLeft = ivec2(int(sampleDistance * cos(angle + sampleAngleRads)), int(sampleDistance * sin(angle + sampleAngleRads)));
	ivec2 offsetStraight = ivec2(int(sampleDistance * cos(angle)), int(sampleDistance * sin(angle)));
	ivec2 offsetRight = ivec2(int(sampleDistance * cos(angle - sampleAngleRads)), int(sampleDistance * sin(angle - sampleAngleRads)));

	ivec2 leftPoint = ivec2(position) + offsetLeft;
	ivec2 straightPoint = ivec2(position) + offsetStraight;
	ivec2 rightPoint = ivec2(position) + offsetRight;

	if (distance(vec2(leftPoint), UV / SCREEN_PIXEL_SIZE) < sampleRadius) {
		COLOR = vec4(1, 0, 0, 1);
	} else if (distance(vec2(straightPoint), UV / SCREEN_PIXEL_SIZE) < sampleRadius) {
		COLOR = vec4(0, 1, 0, 1);
	} else if (distance(vec2(rightPoint), UV / SCREEN_PIXEL_SIZE) < sampleRadius) {
		COLOR = vec4(0, 0, 1, 1);
	} else if (distance(vec2(position), UV / SCREEN_PIXEL_SIZE) < sampleRadius) {
		COLOR = vec4(1, 1, 1, 1);
	}

}

//void light() {
	// Called for every pixel for every light affecting the CanvasItem.
	// Uncomment to replace the default light processing function with this one.
//}
