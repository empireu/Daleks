using System.Numerics;
using System.Runtime.InteropServices;
using Common;
using GameFramework.Extensions;
using GameFramework.Renderer;
using GameFramework.Renderer.Batch;
using GameFramework.Utilities.Extensions;

namespace Vizulacru;

internal sealed class ParticleCollection
{
    private readonly List<Particle> _particles = new();

    public Span<Particle> Span => CollectionsMarshal.AsSpan(_particles);

    public void AddParticle(Particle particle)
    {
        _particles.Add(particle);
    }

    public void Update(float dt)
    {
        var span = Span;

        for (var i = 0; i < _particles.Count; i++)
        {
            ref var particle = ref span[i];

            particle.Update(dt);

            if (particle.IsDead)
            {
                _particles.RemoveAt(i--);
                span = Span;
            }
        }
    }
}

internal struct Particle
{
    public int ID;
    public Pose2d Transform;
    public Twist2d Velocity;
    public float InitialTime; // Could also store timestamp but CBA
    public float TimeRemaining;
    public float Scale;

    public bool IsDead => TimeRemaining <= 0;

    public Matrix4x4 Matrix => Matrix4x4.CreateScale(Scale, Scale, 0) *
                               Matrix4x4.CreateRotationZ(Transform.Rotation.Log()) *
                               Matrix4x4.CreateTranslation(Transform.Translation.X, Transform.Translation.Y, 0);

    public void Update(float dt)
    {
        Transform = new Pose2d
        {
            Translation = Transform.Translation + Velocity.TransVel * dt,
            Rotation = Transform.Rotation * Rotation2d.Exp(Velocity.RotVel * dt)
        };

        TimeRemaining -= dt;
    }
}

internal interface IParticleMaterial
{
    void Submit(ReadOnlySpan<Particle> particles, QuadBatch batch);
}

internal sealed class TextureFragmentParticleMaterial : IParticleMaterial
{
    public TextureSampler Texture { get; }

    public TextureFragmentParticleMaterial(TextureSampler texture)
    {
        Texture = texture;
    }

    public void Submit(ReadOnlySpan<Particle> particles, QuadBatch batch)
    {
        for (var i = 0; i < particles.Length; i++)
        {
            var particle = particles[i];
            var random = new Random(particle.ID);

            var min = random.NextVector2(min: 0f, max: 1f);
            var sizeX = random.NextFloat(min: 0f, max: 1 - min.X);
            var sizeY = random.NextFloat(min: 0f, max: 1 - min.Y);
            var max = new Vector2(min.X + sizeX, min.Y + sizeY);

            batch.AddTexturedQuad(
                Texture,
                particle.Matrix,
                max,
                new Vector2(max.X, min.Y),
                min,
                new Vector2(min.X, max.Y)
            );
        }
    }
}

internal sealed class ColoredParticleMaterial : IParticleMaterial
{
    public Vector4 BaseColor { get; }
    public Vector4 MinTintAffine { get; }
    public Vector4 MaxTintAffine { get; }
    public float FadeoutExp { get; }

    public ColoredParticleMaterial(Vector4 baseColor, Vector4 minTintAffine, Vector4 maxTintAffine, float fadeoutExp)
    {
        BaseColor = baseColor;
        MinTintAffine = minTintAffine;
        MaxTintAffine = maxTintAffine;
        FadeoutExp = fadeoutExp;
    }

    public void Submit(ReadOnlySpan<Particle> particles, QuadBatch batch)
    {
        var min = MinTintAffine;
        var max = MaxTintAffine;

        for (var i = 0; i < particles.Length; i++)
        {
            var particle = particles[i];
            var random = new Random(particle.ID);
            var progress = particle.TimeRemaining / particle.InitialTime;

            var fade = 1f - MathF.Pow(progress, FadeoutExp);

            var color = new RgbaFloat4(
                BaseColor.X * (1f + random.NextFloat(min: min.X, max: max.X)),
                BaseColor.Y * (1f + random.NextFloat(min: min.Y, max: max.Y)),
                BaseColor.Z * (1f + random.NextFloat(min: min.Z, max: max.Z)),
                BaseColor.W * (1f + random.NextFloat(min: min.W, max: max.W)) * fade
            );

            batch.Quad(
                particle.Transform.Translation, 
                new Vector2(particle.Scale), 
                particle.Transform.Rotation.Log(),
                color
            );
        }
    }
}

internal sealed class ParticleSystem
{
    private readonly Dictionary<IParticleMaterial, ParticleCollection> _particles = new();

    private ParticleCollection Collection(IParticleMaterial material) =>
        _particles.GetOrAdd(material, _ => new ParticleCollection());

    public void Add(IParticleMaterial material, Particle particle)
    {
        Collection(material).AddParticle(particle);
    }

    public void Update(float dt)
    {
        foreach (var collection in _particles.Values)
        {
            collection.Update(dt);
        }
    }

    public void Submit(QuadBatch batch)
    {
        foreach (var (material, collection) in _particles)
        {
            var span = collection.Span;

            material.Submit(span, batch);
        }
    }

    private int _id;

    public void Create(Pose2d transform, Twist2d velocity, float lifeTime, IParticleMaterial material, float scale)
    {
        Add(material, new Particle
        {
            ID = _id++,
            Scale = scale,
            InitialTime = lifeTime,
            TimeRemaining = lifeTime,
            Transform = transform,
            Velocity = velocity
        });
    }
}
