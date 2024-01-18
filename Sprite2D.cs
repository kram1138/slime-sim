using Godot;
using Godot.Collections;
using System;
using System.Drawing;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.Intrinsics.Arm;

struct Particle
{
	public Vector2 Position;
	public Vector2 Velocity;

	public static int Size = sizeof(float) * 4;

	public void CopyToBuffer(byte[] buffer, int i)
	{
		Buffer.BlockCopy(BitConverter.GetBytes(Position.X), 0, buffer, i * Size + 0 * sizeof(float), sizeof(float));
		Buffer.BlockCopy(BitConverter.GetBytes(Position.Y), 0, buffer, i * Size + 1 * sizeof(float), sizeof(float));
		Buffer.BlockCopy(BitConverter.GetBytes(Velocity.X), 0, buffer, i * Size + 2 * sizeof(float), sizeof(float));
		Buffer.BlockCopy(BitConverter.GetBytes(Velocity.Y), 0, buffer, i * Size + 3 * sizeof(float), sizeof(float));
	}

	public static Particle FromBuffer(byte[] buffer, int i)
	{
		return new Particle
		{
			Position = new Vector2(
				BitConverter.ToSingle(buffer, i * Size + 0 * sizeof(float)),
				BitConverter.ToSingle(buffer, i * Size + 1 * sizeof(float))
			),
			Velocity = new Vector2(
				BitConverter.ToSingle(buffer, i * Size + 2 * sizeof(float)),
				BitConverter.ToSingle(buffer, i * Size + 3 * sizeof(float))
			)
		};
	}

	public static Particle Random(Random rand)
	{
		var angle = (float)rand.NextDouble() * Mathf.Pi * 2;
		return new Particle
		{
			Position = new Vector2((float)rand.NextDouble(), (float)rand.NextDouble()),
			Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))
		};
	}

	public override string ToString()
	{
		return $"({Position.X}, {Position.Y}) ({Velocity.X}, {Velocity.Y})";
	}
}

public partial class Sprite2D : Godot.Sprite2D
{

	int size = 1024;
	int numParticles = 100;

	static Random rand = new Random();
	RenderingDevice rd;

	Rid particleMotionShader;
	RDUniform particleUniform;
	RDUniform particleParamsUniform;

	Rid trailsShader;

	Rid[] textures;
	int currentTexture = 0;
	RDUniform[][] trailTextureUniforms;
	RDUniform trailsParamsUniform;

	byte[] CreateTrailParams(float delta, float diffuseRate, float particleSize)
	{
		var buffer = new byte[sizeof(float) * 3];
		Buffer.BlockCopy(BitConverter.GetBytes(delta), 0, buffer, 0 * sizeof(float), sizeof(float));
		Buffer.BlockCopy(BitConverter.GetBytes(diffuseRate), 0, buffer, 1 * sizeof(float), sizeof(float));
		Buffer.BlockCopy(BitConverter.GetBytes(particleSize), 0, buffer, 2 * sizeof(float), sizeof(float));

		return buffer;
	}

	byte[] CreateParticleParams(float delta, float speed)
	{
		var buffer = new byte[sizeof(float) * 2];
		Buffer.BlockCopy(BitConverter.GetBytes(delta), 0, buffer, 0 * sizeof(float), sizeof(float));
		Buffer.BlockCopy(BitConverter.GetBytes(speed), 0, buffer, 1 * sizeof(float), sizeof(float));
		return buffer;
	}

	void RunShader(RenderingDevice rd, uint xGroups, uint yGroups, Rid shader, Rid uniformSet)
	{
		// Create a compute pipeline
		var pipeline = rd.ComputePipelineCreate(shader);
		var computeList = rd.ComputeListBegin();
		rd.ComputeListBindComputePipeline(computeList, pipeline);
		rd.ComputeListBindUniformSet(computeList, uniformSet, 0);
		rd.ComputeListDispatch(computeList, xGroups: xGroups, yGroups: yGroups, zGroups: 1);
		rd.ComputeListEnd();
	}

	byte[] ReadBuffer(RenderingDevice rd, Rid buffer, int inputLength)
	{
		var outputBytes = rd.BufferGetData(buffer);
		var output = new float[inputLength * 4];
		Buffer.BlockCopy(outputBytes, 0, output, 0, outputBytes.Length);
		return outputBytes;
	}

	Rid CreateComputeShader(RenderingDevice rd, string path)
	{
		var shaderFile = GD.Load<RDShaderFile>(path);
		var shaderBytecode = shaderFile.GetSpirV();
		return rd.ShaderCreateFromSpirV(shaderBytecode);
	}

	Rid CreateBuffer(RenderingDevice rd, byte[] data)
	{
		return rd.StorageBufferCreate((uint)data.Length, data);
	}

	RDUniform CreateBufferUniform(Rid buffer, int binding)
	{
		var uniform = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.StorageBuffer,
			Binding = binding
		};
		uniform.AddId(buffer);
		return uniform;
	}

	void UpdateBufferUniform(RDUniform uniform, Rid buffer)
	{
		uniform.ClearIds();
		uniform.AddId(buffer);
	}

	Rid CreateTexture(RenderingDevice rd, int width, int height)
	{
		var fmt = new RDTextureFormat
		{
			Width = (uint)width,
			Height = (uint)height,
			Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat,
			UsageBits = RenderingDevice.TextureUsageBits.CanUpdateBit |
						RenderingDevice.TextureUsageBits.StorageBit |
						RenderingDevice.TextureUsageBits.CanCopyFromBit |
						RenderingDevice.TextureUsageBits.SamplingBit
		};

		var view = new RDTextureView();
		var image = Image.Create(width, height, false, Image.Format.Rgbaf);
		image.Fill(new Godot.Color(0, 0, 0, 0));
		var imageData = new Array<byte[]>(new byte[][] { image.GetData() });
		return rd.TextureCreate(fmt, view, imageData);
	}

	RDUniform CreateTextureUniform(Rid texture, int binding)
	{
		var uniform = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.Image,
			Binding = binding
		};
		uniform.AddId(texture);
		return uniform;
	}

	RDUniform CreateSamplerUniform(RenderingDevice rd, Rid texture, int binding)
	{
		var sampler = CreateSampler(rd);
		var uniform = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.SamplerWithTexture,
			Binding = binding
		};
		uniform.AddId(sampler);
		uniform.AddId(texture);
		return uniform;
	}

	Rid CreateUniformSet(RenderingDevice rd, Rid shader, params RDUniform[] uniforms)
	{
		var uniformSet = rd.UniformSetCreate(new Array<RDUniform>(uniforms), shader, 0);
		return uniformSet;
	}

	Rid CreateSampler(RenderingDevice rd)
	{
		var samplerState = new RDSamplerState();
		return rd.SamplerCreate(samplerState);
	}

	Image GetShaderTexture(RenderingDevice rd, Rid texture, int width, int height)
	{
		var bytes = rd.TextureGetData(texture, 0);
		return Image.CreateFromData(width, height, false, Image.Format.Rgbaf, bytes);
	}

	public override void _Ready()
	{
		RenderingServer.CallOnRenderThread(Callable.From(SetupSim));
	}

	public override void _Process(double delta)
	{
		RenderingServer.CallOnRenderThread(Callable.From(() => RunSim(delta)));
	}

	public void SetupSim()
	{
		rd = RenderingServer.GetRenderingDevice();

		var input = Enumerable.Range(0, numParticles).Select(_ => Particle.Random(rand)).ToArray();
		var inputBytes = new byte[input.Length * Particle.Size];
		for (int i = 0; i < input.Length; i++)
			input[i].CopyToBuffer(inputBytes, i);

		particleMotionShader = CreateComputeShader(rd, "res://particles.glsl");
		particleUniform = CreateBufferUniform(CreateBuffer(rd, inputBytes), 0);
		var particleParamsBuffer = CreateBuffer(rd, CreateParticleParams(0.0f, 1f));
		particleParamsUniform = CreateBufferUniform(particleParamsBuffer, 1);

		trailsShader = CreateComputeShader(rd, "res://trails.glsl");
		var trailsParamsBuffer = CreateBuffer(rd, CreateTrailParams(0f, 0.01f, 0.001f));
		trailsParamsUniform = CreateBufferUniform(trailsParamsBuffer, 3);
		textures = new Rid[] {
			CreateTexture(rd, size, size),
			CreateTexture(rd, size, size),
		};
		trailTextureUniforms = new RDUniform[][] {
			new RDUniform[] {
				CreateTextureUniform(textures[0], 1),
				CreateTextureUniform(textures[1], 2),
			},
			new RDUniform[] {
				CreateTextureUniform(textures[1], 1),
				CreateTextureUniform(textures[0], 2),
			}
		};

		Texture = new Texture2Drd();
		(Texture as Texture2Drd).TextureRdRid = textures[currentTexture];
	}

	public void RunSim(double delta)
	{
		var particleParams = CreateParticleParams(
			(float)delta,
			(float)GetParent().GetNode<Slider>("SpeedSlider").Value
		);
		UpdateBufferUniform(particleParamsUniform, CreateBuffer(rd, particleParams));

		var particleUniformSet = CreateUniformSet(rd, particleMotionShader, particleUniform, particleParamsUniform);
		RunShader(rd, (uint)numParticles, 1, particleMotionShader, particleUniformSet);
		// rd.Barrier(RenderingDevice.BarrierMask.Compute);

		var trailsParams = CreateTrailParams(
			(float)delta,
			(float)GetParent().GetNode<Slider>("DiffuseRateSlider").Value,
			(float)GetParent().GetNode<Slider>("ParticleSizeSlider").Value
		);
		UpdateBufferUniform(trailsParamsUniform, CreateBuffer(rd, trailsParams));


		var uniformSets = new Rid[] {
			CreateUniformSet(rd,trailsShader,
				particleUniform,
				trailTextureUniforms[currentTexture][0],
				trailTextureUniforms[currentTexture][1],
				trailsParamsUniform
			),
			CreateUniformSet(rd, trailsShader,
				particleUniform,
				trailTextureUniforms[currentTexture][0],
				trailTextureUniforms[currentTexture][1],
				trailsParamsUniform
			),
		};

		RunShader(rd, (uint)size / 32, (uint)size / 32, trailsShader, uniformSets[currentTexture]);
		currentTexture = 1 - currentTexture;
		(Texture as Texture2Drd).TextureRdRid = textures[currentTexture];
	}

	public void FreeSim()
	{
		rd.FreeRid(particleMotionShader);
		// rd.FreeRid(particleUniformSet);
		rd.FreeRid(trailsShader);
		foreach (var texture in textures)
			rd.FreeRid(texture);
	}
}
