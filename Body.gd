@tool
extends Node2D

@export var radius = 10.0:
	set(r):
		radius = r
		queue_redraw()

@export var color: Color:
	set(c):
		color = c
		queue_redraw()

# Called when the node enters the scene tree for the first time.
func _draw():
	var i := Image.create()
	draw_circle(position, radius, color)
