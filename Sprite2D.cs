using Godot;
using Godot.Collections;
using System;
using System.Linq;


public static class ComputeUtils
{
	public static void RunShader(RenderingDevice rd, uint xGroups, uint yGroups, Rid shader, Rid uniformSet)
	{
		// Create a compute pipeline
		var pipeline = rd.ComputePipelineCreate(shader);
		var computeList = rd.ComputeListBegin();
		rd.ComputeListBindComputePipeline(computeList, pipeline);
		rd.ComputeListBindUniformSet(computeList, uniformSet, 0);
		rd.ComputeListDispatch(computeList, xGroups: xGroups, yGroups: yGroups, zGroups: 1);
		rd.ComputeListEnd();
	}

	public static byte[] ReadBuffer(RenderingDevice rd, Rid buffer, int inputLength)
	{
		var outputBytes = rd.BufferGetData(buffer);
		var output = new float[inputLength * 4];
		Buffer.BlockCopy(outputBytes, 0, output, 0, outputBytes.Length);
		return outputBytes;
	}

	public static Rid CreateComputeShader(RenderingDevice rd, string path)
	{
		var shaderFile = GD.Load<RDShaderFile>(path);
		var shaderBytecode = shaderFile.GetSpirV();
		return rd.ShaderCreateFromSpirV(shaderBytecode);
	}

	public static Rid CreateBuffer(RenderingDevice rd, byte[] data)
	{
		return rd.StorageBufferCreate((uint)data.Length, data);
	}

	public static RDUniform CreateBufferUniform(Rid buffer, int binding)
	{
		var uniform = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.StorageBuffer,
			Binding = binding
		};
		uniform.AddId(buffer);
		return uniform;
	}

	public static void UpdateBufferUniform(RDUniform uniform, Rid buffer)
	{
		uniform.ClearIds();
		uniform.AddId(buffer);
	}

	public static Rid CreateTexture(RenderingDevice rd, int width, int height)
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
		image.Fill(new Color(0, 0, 0, 1.0f));
		var imageData = new Array<byte[]>(new byte[][] { image.GetData() });
		return rd.TextureCreate(fmt, view, imageData);
	}

	public static RDUniform CreateTextureUniform(Rid texture, int binding)
	{
		var uniform = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.Image,
			Binding = binding
		};
		uniform.AddId(texture);
		return uniform;
	}

	public static RDUniform CreateSamplerUniform(RenderingDevice rd, Rid texture, int binding)
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

	public static Rid CreateUniformSet(RenderingDevice rd, Rid shader, params RDUniform[] uniforms)
	{
		var uniformSet = rd.UniformSetCreate(new Array<RDUniform>(uniforms), shader, 0);
		return uniformSet;
	}

	public static Rid CreateSampler(RenderingDevice rd)
	{
		var samplerState = new RDSamplerState();
		return rd.SamplerCreate(samplerState);
	}

	public static Image GetShaderTexture(RenderingDevice rd, Rid texture, int width, int height)
	{
		var bytes = rd.TextureGetData(texture, 0);
		return Image.CreateFromData(width, height, false, Image.Format.Rgbaf, bytes);
	}
}

public class FrameBuffer
{
	RenderingDevice rd;
	int currentTexture = 0;

	public Rid[] textures;

	public FrameBuffer(RenderingDevice rd, int width, int height)
	{
		this.rd = rd;
		textures = new Rid[] {
			ComputeUtils.CreateTexture(rd, width, height),
			ComputeUtils.CreateTexture(rd, width, height)
		};
	}

	public void Swap()
	{
		currentTexture = 1 - currentTexture;
	}

	public Rid outputTexture => textures[currentTexture];

	public void Free()
	{
		foreach (var texture in textures)
			rd.FreeRid(texture);
	}
}

public interface ComputeParams
{
	byte[] ToBytes();
}

public class DataStage<T> where T : ComputeParams
{
	RenderingDevice rd;
	Rid shader;
	Rid dataBuffer;
	RDUniform dataBufferUniform;
	RDUniform paramsUniform;
	public DataStage(
		RenderingDevice rd,
		string ShaderPath,
		int dataBufferBinding,
		byte[] startingBytes,
		int paramsBinding,
		T initialParams
	)
	{
		this.rd = rd;
		shader = ComputeUtils.CreateComputeShader(rd, ShaderPath);
		dataBufferUniform = ComputeUtils.CreateBufferUniform(ComputeUtils.CreateBuffer(rd, startingBytes), dataBufferBinding);

		var paramBytes = initialParams.ToBytes();
		var paramBuffer = ComputeUtils.CreateBuffer(rd, paramBytes);
		paramsUniform = ComputeUtils.CreateBufferUniform(paramBuffer, paramsBinding);
	}

	public void RunStage(T param, uint xGroups, uint yGroups, params RDUniform[] otherInputs)
	{
		var paramBytes = param.ToBytes();
		ComputeUtils.UpdateBufferUniform(paramsUniform, ComputeUtils.CreateBuffer(rd, paramBytes));

		var uniforms = otherInputs.Concat(new RDUniform[] {
			dataBufferUniform,
			paramsUniform
		}).ToArray();
		var uniformSet = ComputeUtils.CreateUniformSet(rd, shader, uniforms);
		ComputeUtils.RunShader(rd, xGroups, yGroups, shader, uniformSet);
	}

	public RDUniform outputBuffer => dataBufferUniform;

	public void Free()
	{
		rd.FreeRid(shader);
		rd.FreeRid(dataBuffer);

	}
}

public class RenderStage<T> where T : ComputeParams
{
	RenderingDevice rd;
	Rid shader;
	FrameBuffer frameBuffer;
	RDUniform[][] textureUniforms;
	RDUniform paramsUniform;
	int currentTexture = 0;

	public RenderStage(
		RenderingDevice rd,
		string ShaderPath,
		FrameBuffer frameBuffer,
		int[] textureBindings,
		int paramsBinding,
		T initialParams,
		bool offset = false
	)
	{
		if (offset)
			currentTexture = 1;
		this.rd = rd;
		shader = ComputeUtils.CreateComputeShader(rd, ShaderPath);

		this.frameBuffer = frameBuffer;
		textureUniforms = new RDUniform[][] {
			new RDUniform[] {
				ComputeUtils.CreateTextureUniform(frameBuffer.textures[0], textureBindings[0]),
				ComputeUtils.CreateTextureUniform(frameBuffer.textures[1], textureBindings[1]),
			},
			new RDUniform[] {
				ComputeUtils.CreateTextureUniform(frameBuffer.textures[1], textureBindings[0]),
				ComputeUtils.CreateTextureUniform(frameBuffer.textures[0], textureBindings[1]),
			}
		};
		var paramBytes = initialParams.ToBytes();
		var paramBuffer = ComputeUtils.CreateBuffer(rd, paramBytes);
		paramsUniform = ComputeUtils.CreateBufferUniform(paramBuffer, paramsBinding);
	}

	public void RunStage(T param, uint xGroups, uint yGroups, params RDUniform[] otherInputs)
	{
		var paramBytes = param.ToBytes();
		ComputeUtils.UpdateBufferUniform(paramsUniform, ComputeUtils.CreateBuffer(rd, paramBytes));

		var uniforms = otherInputs.Concat(new RDUniform[] {
			textureUniforms[currentTexture][0],
			textureUniforms[currentTexture][1],
			paramsUniform
		}).ToArray();
		var uniformSet = ComputeUtils.CreateUniformSet(
			rd,
			shader,
			uniforms
		);

		ComputeUtils.RunShader(rd, xGroups, yGroups, shader, uniformSet);
		frameBuffer.Swap();
	}

	public void Free()
	{
		rd.FreeRid(shader);
	}
}

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

struct ParticleParams : ComputeParams
{
	public double Delta;
	public double Speed;
	public double RotationRate;
	public double SampleRadius;
	public double SampleDistance;
	public double SampleAngle;

	public byte[] ToBytes()
	{
		var buffer = new byte[sizeof(float) * 6];
		Buffer.BlockCopy(BitConverter.GetBytes((float)Delta), 0, buffer, 0 * sizeof(float), sizeof(float));
		Buffer.BlockCopy(BitConverter.GetBytes((float)Speed), 0, buffer, 1 * sizeof(float), sizeof(float));
		Buffer.BlockCopy(BitConverter.GetBytes((float)RotationRate), 0, buffer, 2 * sizeof(float), sizeof(float));
		Buffer.BlockCopy(BitConverter.GetBytes((float)SampleRadius), 0, buffer, 3 * sizeof(float), sizeof(float));
		Buffer.BlockCopy(BitConverter.GetBytes((float)SampleDistance), 0, buffer, 4 * sizeof(float), sizeof(float));
		Buffer.BlockCopy(BitConverter.GetBytes((float)SampleAngle), 0, buffer, 5 * sizeof(float), sizeof(float));

		return buffer;
	}
}

struct TrailsParams : ComputeParams
{
	public double Delta;
	public double DecayRate;
	public double ParticleSize;

	public byte[] ToBytes()
	{
		var buffer = new byte[sizeof(float) * 3];
		Buffer.BlockCopy(BitConverter.GetBytes((float)Delta), 0, buffer, 0 * sizeof(float), sizeof(float));
		Buffer.BlockCopy(BitConverter.GetBytes((float)DecayRate), 0, buffer, 1 * sizeof(float), sizeof(float));
		Buffer.BlockCopy(BitConverter.GetBytes((float)ParticleSize), 0, buffer, 2 * sizeof(float), sizeof(float));

		return buffer;
	}
}

struct DiffuseParams : ComputeParams
{
	public double Delta;
	public double DiffuseRate;

	public byte[] ToBytes()
	{
		var buffer = new byte[sizeof(float) * 2];
		Buffer.BlockCopy(BitConverter.GetBytes((float)Delta), 0, buffer, 0 * sizeof(float), sizeof(float));
		Buffer.BlockCopy(BitConverter.GetBytes((float)DiffuseRate), 0, buffer, 1 * sizeof(float), sizeof(float));

		return buffer;
	}
}

public partial class Sprite2D : Godot.Sprite2D
{

	int size = 1024;
	int numParticles = 1024;

	static Random rand = new Random();
	RenderingDevice rd;

	FrameBuffer frameBuffer;

	DataStage<ParticleParams> particleStage;

	RenderStage<TrailsParams> trailsStage;

	RenderStage<DiffuseParams> diffuseStage;

	RDUniform[] textureUniforms;

public override void _Ready()
{
	RenderingServer.CallOnRenderThread(Callable.From(SetupSim));
}

public override void _Process(double delta)
{
	RenderingServer.CallOnRenderThread(Callable.From(() => RunSim(delta)));
}

public override void _ExitTree()
{
	RenderingServer.CallOnRenderThread(Callable.From(FreeSim));
}

public void SetupSim()
{
	rd = RenderingServer.GetRenderingDevice();

	frameBuffer = new FrameBuffer(rd, size, size);

	var input = Enumerable.Range(0, numParticles).Select(_ => Particle.Random(rand)).ToArray();
	var inputBytes = new byte[input.Length * Particle.Size];
	for (int i = 0; i < input.Length; i++)
		input[i].CopyToBuffer(inputBytes, i);

	var particleParams = new ParticleParams
	{
		Delta = 0.0,
		Speed = GetParent().GetNode<Slider>("SpeedSlider").Value,
		RotationRate = GetParent().GetNode<Slider>("RotationRateSlider").Value,
		SampleRadius = GetParent().GetNode<Slider>("SampleRadiusSlider").Value,
		SampleDistance = GetParent().GetNode<Slider>("SampleDistanceSlider").Value,
		SampleAngle = GetParent().GetNode<Slider>("SampleAngleSlider").Value
	};
	particleStage = new DataStage<ParticleParams>(rd, "res://particles.glsl", 0, inputBytes, 1, particleParams);
	textureUniforms = new RDUniform[] {
		ComputeUtils.CreateTextureUniform(frameBuffer.textures[0], 2),
		ComputeUtils.CreateTextureUniform(frameBuffer.textures[1], 2),
	};

	var trailsParams = new TrailsParams
	{
		Delta = 0.0f,
		DecayRate = GetParent().GetNode<Slider>("DecayRateSlider").Value,
		ParticleSize = GetParent().GetNode<Slider>("ParticleSizeSlider").Value
	};
	trailsStage = new RenderStage<TrailsParams>(rd, "res://trails.glsl", frameBuffer, new int[] { 1, 2 }, 3, trailsParams);

	var diffuseParams = new DiffuseParams
	{
		Delta = 0.0f,
		DiffuseRate = GetParent().GetNode<Slider>("DiffuseRateSlider").Value
	};
	diffuseStage = new RenderStage<DiffuseParams>(rd, "res://diffuse.glsl", frameBuffer, new int[] { 0, 1 }, 2, diffuseParams, true);

	Texture = new Texture2Drd();
	(Texture as Texture2Drd).TextureRdRid = frameBuffer.outputTexture;
}

public void RunSim(double delta)
{
	var particleParams = new ParticleParams
	{
		Delta = delta,
		Speed = GetParent().GetNode<Slider>("SpeedSlider").Value,
		RotationRate = GetParent().GetNode<Slider>("RotationRateSlider").Value,
		SampleRadius = GetParent().GetNode<Slider>("SampleRadiusSlider").Value,
		SampleDistance = GetParent().GetNode<Slider>("SampleDistanceSlider").Value,
		SampleAngle = GetParent().GetNode<Slider>("SampleAngleSlider").Value
	};

	particleStage.RunStage(particleParams, (uint)numParticles / 16, 1, textureUniforms[0]);

	// rd.Barrier(RenderingDevice.BarrierMask.Compute);

	var trailsParams = new TrailsParams
	{
		Delta = delta,
		DecayRate = GetParent().GetNode<Slider>("DecayRateSlider").Value,
		ParticleSize = GetParent().GetNode<Slider>("ParticleSizeSlider").Value
	};

	trailsStage.RunStage(trailsParams, (uint)size / 16, (uint)size / 16, particleStage.outputBuffer);

	// rd.Barrier(RenderingDevice.BarrierMask.Compute);

	var diffuseParams = new DiffuseParams
	{
		Delta = delta,
		DiffuseRate = GetParent().GetNode<Slider>("DiffuseRateSlider").Value
	};

	diffuseStage.RunStage(diffuseParams, (uint)size / 16, (uint)size / 16);

	(Texture as Texture2Drd).TextureRdRid = frameBuffer.outputTexture;
}

public void FreeSim()
{
	particleStage.Free();
	trailsStage.Free();
}
}
