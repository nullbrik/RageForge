/* RAGEFORGE - Model Viewport */

using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using RageForge.Core.Model;
using RageForge.Core.Ttpf;
using RageForge.Core.World;

namespace RageForge.Gui.Controls;

public sealed class ModelViewport : OpenGlControlBase, Avalonia.Rendering.ICustomHitTest
{
    public static readonly StyledProperty<IReadOnlyList<DecodedMesh>?> MeshesProperty =
        AvaloniaProperty.Register<ModelViewport, IReadOnlyList<DecodedMesh>?>(nameof(Meshes));

    public static readonly StyledProperty<Scene?> SceneProperty =
        AvaloniaProperty.Register<ModelViewport, Scene?>(nameof(Scene));

    public static readonly StyledProperty<Vector4> SceneFocusProperty =
        AvaloniaProperty.Register<ModelViewport, Vector4>(nameof(SceneFocus));

    public static readonly StyledProperty<double> TimeOfDayProperty =
        AvaloniaProperty.Register<ModelViewport, double>(nameof(TimeOfDay), 13.0);

    public static readonly StyledProperty<bool> ShowWaterProperty =
        AvaloniaProperty.Register<ModelViewport, bool>(nameof(ShowWater), true);

    private const int GL_ARRAY_BUFFER = 0x8892;
    private const int GL_ELEMENT_ARRAY_BUFFER = 0x8893;
    private const int GL_STATIC_DRAW = 0x88E4;
    private const int GL_FLOAT = 0x1406;
    private const int GL_UNSIGNED_INT = 0x1405;
    private const int GL_UNSIGNED_SHORT = 0x1403;
    private const int GL_BYTE = 0x1400;
    private const int GL_UNSIGNED_BYTE = 0x1401;
    private const int GL_TRIANGLES = 0x0004;
    private const int GL_LINES = 0x0001;
    private const int GL_TEXTURE_2D = 0x0DE1;
    private const int GL_TEXTURE0 = 0x84C0;
    private const int GL_TEXTURE1 = 0x84C1;
    private const int GL_RGBA = 0x1908;
    private const int GL_RGBA8 = 0x8058;
    private const int GL_DEPTH_COMPONENT24 = 0x81A6;
    private const int GL_TEXTURE_MIN_FILTER = 0x2801;
    private const int GL_TEXTURE_MAG_FILTER = 0x2800;
    private const int GL_TEXTURE_WRAP_S = 0x2802;
    private const int GL_TEXTURE_WRAP_T = 0x2803;
    private const int GL_LINEAR = 0x2601;
    private const int GL_NEAREST = 0x2600;
    private const int GL_LINEAR_MIPMAP_LINEAR = 0x2703;
    private const int GL_REPEAT = 0x2901;
    private const int GL_DEPTH_TEST = 0x0B71;
    private const int GL_LEQUAL = 0x0203;
    private const int GL_COLOR_BUFFER_BIT = 0x4000;
    private const int GL_DEPTH_BUFFER_BIT = 0x0100;
    private const int GL_FRAMEBUFFER = 0x8D40;
    private const int GL_READ_FRAMEBUFFER = 0x8CA8;
    private const int GL_DRAW_FRAMEBUFFER = 0x8CA9;
    private const int GL_RENDERBUFFER = 0x8D41;
    private const int GL_COLOR_ATTACHMENT0 = 0x8CE0;
    private const int GL_DEPTH_ATTACHMENT = 0x8D00;
    private const int GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    private const int GL_VERTEX_SHADER = 0x8B31;
    private const int GL_FRAGMENT_SHADER = 0x8B30;
    private const int GL_BLEND = 0x0BE2;
    private const int GL_SRC_ALPHA = 0x0302;
    private const int GL_ONE_MINUS_SRC_ALPHA = 0x0303;
    private const int GL_POLYGON_OFFSET_FILL = 0x8037;
    private const int GL_STENCIL_TEST = 0x0B90;
    private const int GL_STENCIL_BUFFER_BIT = 0x0400;
    private const int GL_DEPTH24_STENCIL8 = 0x88F0;
    private const int GL_DEPTH_STENCIL_ATTACHMENT = 0x821A;
    private const int GL_GEQUAL = 0x0206;
    private const int GL_KEEP = 0x1E00;
    private const int GL_REPLACE = 0x1E01;
    private const int GL_TEXTURE_MAX_LEVEL = 0x813D;
    private const int GL_VERSION = 0x1F02;
    private const int GL_RENDERER = 0x1F01;
    private const int GL_EXTENSIONS = 0x1F03;
    private const int GL_NUM_EXTENSIONS = 0x821D;
    private const int GL_COMPRESSED_RGBA_S3TC_DXT1 = 0x83F1;
    private const int GL_COMPRESSED_RGBA_S3TC_DXT3 = 0x83F2;
    private const int GL_COMPRESSED_RGBA_S3TC_DXT5 = 0x83F3;
    private const int GL_COMPRESSED_RED_RGTC1 = 0x8DBB;
    private const int GL_COMPRESSED_RG_RGTC2 = 0x8DBD;
    private const int GL_COMPRESSED_RGBA_BPTC_UNORM = 0x8E8C;
    private const float DetailRange = 400f;
    private const float DetailScale = 128f * 512f;
    private const uint DetailMax = 1024;
    private const long DetailBudget = 384L << 20;
    private const int DetailInFlight = 4;
    private const double DetailInterval = 0.25;
    private const double DetailLinger = 2.0;
    private const double DetailSettle = 0.05;
    private const int MaxDetailLevel = 4;
    private const int Samples = 4;
    private const int Stride = SceneBatcher.Stride * sizeof(float);
    private const float SmallDistance = 120f;
    private const double UploadBudgetMs = 6;
    private const float WaterLevel = 0f;
    private const double CollectInterval = 10;
    private bool _layered;
    private float _viewRadius = 3000f;
    private float _groundY;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GenerateMipmapFn(int target);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void Uniform4fFn(int location, float x, float y, float z, float w);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void RenderbufferStorageMultisampleFn(int target, int samples, int format, int width, int height);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void BlendFuncFn(int sfactor, int dfactor);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void DrawElementsInstancedFn(int mode, int count, int type, IntPtr indices, int instanceCount);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VertexAttribDivisorFn(int index, int divisor);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void PolygonOffsetFn(float factor, float units);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void StencilFuncFn(int func, int reference, uint mask);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void StencilOpFn(int stencilFail, int depthFail, int depthPass);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ClearStencilFn(int value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void CompressedTexImage2DFn(int target, int level, int internalFormat, int width, int height, int border, int imageSize, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetStringiFn(int name, uint index);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void TexStorage2DFn(int target, int levels, int internalFormat, int width, int height);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void CompressedTexSubImage2DFn(int target, int level, int x, int y, int width, int height, int format, int imageSize, IntPtr data);

    private sealed class GpuTexture(object key)
    {
        public object Key { get; } = key;
        public int Name;
        public int Refs;
        public ulong Hash;
        public uint Base;
        public uint Size;
        public uint Ceiling = uint.MaxValue;
        public uint Wanted;
        public uint Pending;
        public long Bytes;
        public double LastWanted;
        public bool Dead;
    }

    private readonly record struct DetailRequest(GpuTexture Texture, uint Size);

    private readonly record struct DetailResult(GpuTexture Texture, uint Size, MipChain? Chain);

    private sealed record GpuMesh(int Vao, int Vbo, int Ibo, int Count, GpuTexture? Texture, GpuTexture? Normal, bool Cutout, bool Blend)
    {
        public GpuTexture? Mask { get; init; }
        public GpuTexture[] Layers { get; init; } = [];
        public int IndexType { get; init; } = GL_UNSIGNED_INT;
    }

    private sealed record GpuBatch(GpuMesh Mesh, Vector3 Center, float Radius, bool Small, int Triangles);

    private sealed record GpuGroup(List<(int Vao, GpuMesh Mesh)> Meshes, int InstanceVbo, int Count, Vector3 Center, float Radius, bool Small);

    private sealed class Chunk
    {
        public readonly List<GpuBatch> Batches = [];
        public readonly List<GpuGroup> Groups = [];
        public readonly List<GpuMesh> OwnedMeshes = [];
        public readonly List<object> Textures = [];
        public int Level;
    }

    private sealed class Upload(object key, Scene scene, Chunk chunk, IEnumerator<bool> steps)
    {
        public object Key { get; } = key;
        public Scene Scene { get; } = scene;
        public Chunk Chunk { get; } = chunk;
        public IEnumerator<bool> Steps { get; } = steps;
    }

    private readonly Dictionary<object, Chunk> _chunks = [];
    private readonly ConcurrentQueue<(object Key, Scene? Scene, int Level)> _pending = new();
    private readonly List<(object Key, Scene Scene, int Level)> _work = [];
    private Upload? _upload;
    private readonly Dictionary<object, GpuTexture> _textures = [];
    private readonly HashSet<GpuTexture> _streamed = [];
    private readonly Channel<DetailRequest> _detailQueue = Channel.CreateUnbounded<DetailRequest>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentQueue<DetailResult> _detailDone = new();
    private Task? _detailWorker;
    private int _detailInFlight;
    private double _detailAt = double.NegativeInfinity;
    private Vector3 _detailEye = new(float.NaN);
    private bool _detailDirty;
    private long _textureBytes, _detailBytes;
    private CompressedTexImage2DFn? _compressedTexImage;
    private TexStorage2DFn? _texStorage;
    private CompressedTexSubImage2DFn? _compressedTexSubImage;
    private Dictionary<TtpfFormat, int> _native = [];
    private double _lastCollect;
    private readonly HashSet<Key> _keys = [];
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly System.Diagnostics.Stopwatch _frameWatch = new();
    private readonly string? _benchLog = Environment.GetEnvironmentVariable("RAGEFORGE_BENCH");
    private readonly bool _benchFly = Environment.GetEnvironmentVariable("RAGEFORGE_BENCH_STILL") is null;
    private static readonly object SingleKey = new();

    private GenerateMipmapFn? _generateMipmap;
    private Uniform4fFn? _uniform4f;
    private RenderbufferStorageMultisampleFn? _storageMs;
    private BlendFuncFn? _blendFunc;
    private PolygonOffsetFn? _polygonOffset;
    private StencilFuncFn? _stencilFunc;
    private StencilOpFn? _stencilOp;
    private ClearStencilFn? _clearStencil;
    private DrawElementsInstancedFn? _drawInstanced;
    private VertexAttribDivisorFn? _attribDivisor;
    private int _program, _gridProgram, _gridVao, _gridVbo, _gridCount;
    private int _uMvp, _uModel, _uInstanced, _uTex, _uNormalTex, _uHasTex, _uHasNormal, _uCutout, _uBlend, _uLight, _uEye, _gMvp;
    private int _uLightColor, _uFill, _uSkyAmbient, _uGroundAmbient, _uFog;
    private int _uLayers, _uMask;
    private readonly int[] _uLayer = new int[4];
    private int _skyProgram, _skyVao, _skyVbo, _sInvVp, _sEye, _sSun, _sSunColor, _sZenith, _sHorizon, _sNight;
    private int _waterProgram, _waterVao, _waterVbo, _wVp, _wArea, _wEye, _wLight, _wLightColor, _wZenith, _wHorizon, _wDeep, _wFog;
    private int _msFbo, _msColor, _msDepth, _msW, _msH;
    private bool _msFailed;
    private bool _contextLost;
    private object? _sceneKey;
    private Scene? _uploadedScene;
    private Vector3 _center;
    private float _radius = 1f;
    private float _yaw = 0.7f, _pitch = 0.45f, _distance = 3f;
    private Vector3 _pan;
    private Vector3 _reportedEye = new(float.NaN);
    private Point? _last;
    private bool _panning, _aerial, _streaming;
    private double _lastFrame, _statsAt, _frameMs;
    private int _frames, _drawCalls, _triangles;

    public event Action<string>? StatsUpdated;
    public event Action? SceneLost;
    public event Action? InitFailed;
    public event Action<Vector3, float>? CameraMoved;

    public ModelViewport()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public IReadOnlyList<DecodedMesh>? Meshes
    {
        get => GetValue(MeshesProperty);
        set => SetValue(MeshesProperty, value);
    }

    public Scene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public Vector4 SceneFocus
    {
        get => GetValue(SceneFocusProperty);
        set => SetValue(SceneFocusProperty, value);
    }

    public double TimeOfDay
    {
        get => GetValue(TimeOfDayProperty);
        set => SetValue(TimeOfDayProperty, value);
    }

    public bool ShowWater
    {
        get => GetValue(ShowWaterProperty);
        set => SetValue(ShowWaterProperty, value);
    }

    public bool IsReady { get; private set; }

    public Func<ulong, uint, MipChain?>? TextureSource { get; set; }

    public bool IsNative(TtpfFormat format) => Volatile.Read(ref _native).ContainsKey(format);

    public int ChunkCount => _chunks.Count;

    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneProperty)
        {
            if (_sceneKey is { } old) _pending.Enqueue((old, null, 0));
            _sceneKey = null;
            _uploadedScene = null;
            if (change.GetNewValue<Scene?>() is { } scene)
            {
                _sceneKey = scene;
                _pending.Enqueue((scene, scene, 0));
                _streaming = false;
            }
            FitCamera();
            RequestNextFrameRendering();
        }
        else if (change.Property == TimeOfDayProperty || change.Property == ShowWaterProperty)
        {
            RequestNextFrameRendering();
        }
        else if (change.Property == MeshesProperty)
        {
            _pending.Enqueue((SingleKey, null, 0));
            FitCamera();
            RequestNextFrameRendering();
        }
    }

    public void AddChunk(object key, Scene scene, int level = 0)
    {
        _pending.Enqueue((key, scene, Math.Clamp(level, 0, MaxDetailLevel)));
        RequestNextFrameRendering();
    }

    public void RemoveChunk(object key)
    {
        _pending.Enqueue((key, null, 0));
        RequestNextFrameRendering();
    }

    public void BeginStreaming(Vector3 zUpTarget, float viewRadius, float cameraRadius = 0)
    {
        _streaming = true;
        _aerial = true;
        _viewRadius = viewRadius;
        _groundY = zUpTarget.Z;
        _center = new Vector3(zUpTarget.X, zUpTarget.Z, -zUpTarget.Y);
        _radius = cameraRadius > 0 ? cameraRadius : Math.Clamp(viewRadius * 0.25f, 40f, 140f);
        _reportedEye = new Vector3(float.NaN);
        ResetView();
    }

    public void ResetView()
    {
        _yaw = 0.7f;
        _pan = Vector3.Zero;
        if ((Scene is not null || _streaming) && _aerial)
        {
            _pitch = 0.75f;
            _distance = _radius * 2.2f;
        }
        else if (Scene is not null)
        {
            _pitch = 0.15f;
            _distance = MathF.Min(3f, _radius * 0.5f);
        }
        else
        {
            _pitch = 0.45f;
            _distance = _radius * 2.6f;
        }
        RequestNextFrameRendering();
    }

    private void FitCamera()
    {
        if (Scene is not null && SceneFocus.W > 0)
        {
            _center = new Vector3(SceneFocus.X, SceneFocus.Z, -SceneFocus.Y);
            _radius = SceneFocus.W;
            _aerial = true;
            ResetView();
            return;
        }
        _aerial = false;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        void Add(Vector3 zUp)
        {
            if (!float.IsFinite(zUp.X) || !float.IsFinite(zUp.Y) || !float.IsFinite(zUp.Z)) return;
            var v = new Vector3(zUp.X, zUp.Z, -zUp.Y);
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        if (Scene is { } scene)
        {
            foreach (var inst in scene.Instances)
                Add(Vector3.Transform(scene.Models[inst.Model].Center, inst.Transform));
        }
        else if (Meshes is { } meshes)
        {
            foreach (var m in meshes)
                foreach (var p in m.Positions)
                    Add(new Vector3(p.X, p.Y, p.Z));
        }
        if (min.X > max.X) return;
        _center = (min + max) * 0.5f;
        _radius = MathF.Max(0.01f, (max - min).Length() * 0.5f);
        ResetView();
    }

    private const string VertexShader = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec4 aNormal;
        layout(location = 2) in vec2 aUv;
        layout(location = 7) in vec2 aUv1;
        layout(location = 8) in vec4 aTint;
        layout(location = 3) in vec4 aI0;
        layout(location = 4) in vec4 aI1;
        layout(location = 5) in vec4 aI2;
        layout(location = 6) in vec4 aI3;
        uniform mat4 uMvp;
        uniform mat4 uModel;
        uniform int uInstanced;
        out vec3 vWorld;
        out vec3 vNormal;
        out vec2 vUv;
        out vec2 vUv1;
        out float vAlpha;
        out vec3 vTint;
        void main() {
            vec4 local = vec4(aPos, 1.0);
            vec3 n = aNormal.xyz;
            if (uInstanced == 1) {
                mat4 inst = mat4(aI0, aI1, aI2, aI3);
                local = inst * local;
                n = transpose(inverse(mat3(inst))) * n;
            }
            vWorld = (uModel * local).xyz;
            vNormal = mat3(uModel) * n;
            vUv = aUv;
            vUv1 = aUv1;
            vAlpha = aNormal.w;
            vTint = aTint.rgb;
            gl_Position = uMvp * local;
        }
        """;

    private const string Finish = """
        vec3 finish(vec3 c) {
            c = c / (c + vec3(1.0)) * 1.6;
            return pow(clamp(c, 0.0, 1.0), vec3(1.0 / 2.2));
        }

        vec3 applyFog(vec3 c, vec4 fog, float dist) {
            float f = 1.0 - exp(-pow(dist * fog.w, 2.0));
            return mix(c, fog.rgb, clamp(f, 0.0, 1.0));
        }

        """;

    private const string FragmentShader = """
        in vec3 vWorld;
        in vec3 vNormal;
        in vec2 vUv;
        in vec2 vUv1;
        in float vAlpha;
        in vec3 vTint;
        uniform sampler2D uTex;
        uniform sampler2D uNormalTex;
        uniform sampler2D uMask;
        uniform sampler2D uLayer0;
        uniform sampler2D uLayer1;
        uniform sampler2D uLayer2;
        uniform sampler2D uLayer3;
        uniform int uLayers;
        uniform int uHasTex;
        uniform int uHasNormal;
        uniform int uCutout;
        uniform int uBlend;
        uniform vec4 uLight;
        uniform vec4 uLightColor;
        uniform vec4 uFill;
        uniform vec4 uSkyAmbient;
        uniform vec4 uGroundAmbient;
        uniform vec4 uFog;
        uniform vec4 uEye;
        out vec4 fragColor;

        vec3 perturb(vec3 n, vec3 p, vec2 uv) {
            vec2 xy = texture(uNormalTex, uv).xy * 2.0 - 1.0;
            xy.y = -xy.y;
            vec3 m = vec3(xy, sqrt(max(0.0, 1.0 - dot(xy, xy))));
            vec3 dp1 = dFdx(p);
            vec3 dp2 = dFdy(p);
            vec2 duv1 = dFdx(uv);
            vec2 duv2 = dFdy(uv);
            vec3 dp2perp = cross(dp2, n);
            vec3 dp1perp = cross(n, dp1);
            vec3 t = dp2perp * duv1.x + dp1perp * duv2.x;
            vec3 b = dp2perp * duv1.y + dp1perp * duv2.y;
            float len = max(dot(t, t), dot(b, b));
            if (len < 1e-20) return n;
            float inv = inversesqrt(len);
            return normalize(mat3(t * inv, b * inv, n) * m);
        }

        void main() {
            vec4 base = uHasTex == 1 ? texture(uTex, vUv) : vec4(0.62, 0.64, 0.68, 1.0);
            if (uLayers > 0) {
                vec4 w = texture(uMask, vUv);
                vec3 l0 = texture(uLayer0, vUv1).rgb;
                vec3 l1 = uLayers > 1 ? texture(uLayer1, vUv1).rgb : l0;
                vec3 l2 = uLayers > 2 ? texture(uLayer2, vUv1).rgb : l0;
                vec3 l3 = uLayers > 3 ? texture(uLayer3, vUv1).rgb : l2;
                vec3 near = mix(l0, l1, w.b);
                vec3 far = mix(l2, l3, w.b);
                base = vec4(mix(near, far, w.g), 1.0);
            }
            base.rgb *= vTint;
            if (uCutout == 1 && base.a < 0.5) discard;
            if (uBlend == 1) base.a *= vAlpha;
            if (uBlend == 1 && base.a < 0.02) discard;
            vec3 albedo = pow(base.rgb, vec3(2.2));

            vec3 v = normalize(uEye.xyz - vWorld);
            vec3 n = normalize(vNormal);
            if (dot(n, v) < 0.0) n = -n;
            if (uHasNormal == 1) n = perturb(n, vWorld - uEye.xyz, vUv);

            vec3 key = normalize(uLight.xyz);
            float dKey = max(dot(n, key), 0.0);
            float dFill = max(dot(n, normalize(uFill.xyz)), 0.0) * uFill.w;
            vec3 ambient = mix(uGroundAmbient.rgb, uSkyAmbient.rgb, n.y * 0.5 + 0.5);
            vec3 h = normalize(key + v);
            float spec = pow(max(dot(n, h), 0.0), 48.0) * 0.22 * step(0.0, dKey);
            float rim = pow(1.0 - max(dot(n, v), 0.0), 3.0) * 0.12;

            vec3 color = albedo * (ambient + uLightColor.rgb * dKey + vec3(0.55, 0.65, 0.8) * dFill)
                + uLightColor.rgb * spec + ambient * rim;
            color = applyFog(color, uFog, length(uEye.xyz - vWorld));
            fragColor = vec4(finish(color), uBlend == 1 ? base.a : 1.0);
        }
        """;

    private const string SkyVertexShader = """
        layout(location = 0) in vec2 aPos;
        out vec2 vNdc;
        void main() {
            vNdc = aPos;
            gl_Position = vec4(aPos, 0.0, 1.0);
        }
        """;

    private const string SkyFragmentShader = """
        in vec2 vNdc;
        uniform mat4 uInvVp;
        uniform vec4 uEye;
        uniform vec4 uSun;
        uniform vec4 uSunColor;
        uniform vec4 uZenith;
        uniform vec4 uHorizon;
        uniform vec4 uNight;
        out vec4 fragColor;

        float hash(vec3 p) {
            p = fract(p * 0.3183099 + 0.1);
            p *= 17.0;
            return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        void main() {
            vec4 far = uInvVp * vec4(vNdc, 1.0, 1.0);
            vec3 dir = normalize(far.xyz / far.w - uEye.xyz);
            float h = dir.y;
            vec3 sky = mix(uHorizon.rgb, uZenith.rgb, pow(clamp(h, 0.0, 1.0), 0.45));
            if (h < 0.0) sky = mix(uHorizon.rgb, uHorizon.rgb * 0.45, clamp(-h * 4.0, 0.0, 1.0));
            vec3 sun = normalize(uSun.xyz);
            float s = max(dot(dir, sun), 0.0);
            sky += uSunColor.rgb * (pow(s, 900.0) * 24.0 + pow(s, 12.0) * 0.22 + pow(s, 3.0) * 0.08);
            vec3 moon = -sun;
            float m = max(dot(dir, moon), 0.0);
            sky += vec3(0.75, 0.8, 0.9) * pow(m, 1400.0) * 6.0 * uNight.x;
            if (h > 0.0) {
                vec3 cell = floor(dir * 900.0);
                float star = step(0.9985, hash(cell));
                sky += vec3(star) * smoothstep(0.6, 1.0, uNight.x) * smoothstep(0.05, 0.3, h) * (0.2 + 0.5 * hash(cell + 7.0));
            }
            fragColor = vec4(finish(sky), 1.0);
        }
        """;

    private const string WaterVertexShader = """
        layout(location = 0) in vec2 aPos;
        uniform mat4 uVp;
        uniform vec4 uArea;
        out vec3 vWorld;
        void main() {
            vWorld = vec3(uArea.x + aPos.x * uArea.w, uArea.y, uArea.z + aPos.y * uArea.w);
            gl_Position = uVp * vec4(vWorld, 1.0);
        }
        """;

    private const string WaterFragmentShader = """
        in vec3 vWorld;
        uniform vec4 uEye;
        uniform vec4 uLight;
        uniform vec4 uLightColor;
        uniform vec4 uZenith;
        uniform vec4 uHorizon;
        uniform vec4 uDeep;
        uniform vec4 uFog;
        out vec4 fragColor;

        void main() {
            vec2 p = vWorld.xz;
            vec3 n = normalize(vec3(
                sin(p.x * 0.21 + p.y * 0.13) * 0.035 + sin(p.x * 0.83 - p.y * 0.57) * 0.018 + sin(p.x * 2.7 + p.y * 1.9) * 0.008,
                1.0,
                cos(p.y * 0.19 - p.x * 0.11) * 0.035 + cos(p.y * 0.77 + p.x * 0.49) * 0.018 + cos(p.y * 2.3 - p.x * 2.1) * 0.008));
            vec3 v = normalize(uEye.xyz - vWorld);
            float f = 0.02 + 0.98 * pow(1.0 - max(dot(n, v), 0.0), 5.0);
            vec3 r = reflect(-v, n);
            vec3 sky = mix(uHorizon.rgb, uZenith.rgb, pow(clamp(r.y, 0.0, 1.0), 0.45));
            vec3 spec = uLightColor.rgb * pow(max(dot(r, normalize(uLight.xyz)), 0.0), 350.0) * 5.0;
            vec3 color = mix(uDeep.rgb, sky, f) + spec;
            color = applyFog(color, uFog, length(uEye.xyz - vWorld));
            fragColor = vec4(finish(color), mix(0.82, 1.0, f));
        }
        """;

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            bool es = GlVersion.Type == GlProfileType.OpenGLES;
            string header = es ? "#version 300 es\nprecision highp float;\n" : "#version 330 core\n";

            _program = Link(gl, header + VertexShader, header + Finish + FragmentShader);
            _gridProgram = Link(gl, header + """
                layout(location = 0) in vec3 aPos;
                uniform mat4 uMvp;
                void main() { gl_Position = uMvp * vec4(aPos, 1.0); }
                """, header + """
                out vec4 fragColor;
                void main() { fragColor = vec4(0.42, 0.42, 0.46, 1.0); }
                """);

            _uMvp = gl.GetUniformLocationString(_program, "uMvp");
            _uModel = gl.GetUniformLocationString(_program, "uModel");
            _uInstanced = gl.GetUniformLocationString(_program, "uInstanced");
            _uTex = gl.GetUniformLocationString(_program, "uTex");
            _uNormalTex = gl.GetUniformLocationString(_program, "uNormalTex");
            _uLayers = gl.GetUniformLocationString(_program, "uLayers");
            _uMask = gl.GetUniformLocationString(_program, "uMask");
            for (int k = 0; k < _uLayer.Length; k++)
                _uLayer[k] = gl.GetUniformLocationString(_program, $"uLayer{k}");
            _uHasTex = gl.GetUniformLocationString(_program, "uHasTex");
            _uHasNormal = gl.GetUniformLocationString(_program, "uHasNormal");
            _uCutout = gl.GetUniformLocationString(_program, "uCutout");
            _uBlend = gl.GetUniformLocationString(_program, "uBlend");
            _uLight = gl.GetUniformLocationString(_program, "uLight");
            _uEye = gl.GetUniformLocationString(_program, "uEye");
            _gMvp = gl.GetUniformLocationString(_gridProgram, "uMvp");
            _uLightColor = gl.GetUniformLocationString(_program, "uLightColor");
            _uFill = gl.GetUniformLocationString(_program, "uFill");
            _uSkyAmbient = gl.GetUniformLocationString(_program, "uSkyAmbient");
            _uGroundAmbient = gl.GetUniformLocationString(_program, "uGroundAmbient");
            _uFog = gl.GetUniformLocationString(_program, "uFog");

            _skyProgram = Link(gl, header + SkyVertexShader, header + Finish + SkyFragmentShader);
            _sInvVp = gl.GetUniformLocationString(_skyProgram, "uInvVp");
            _sEye = gl.GetUniformLocationString(_skyProgram, "uEye");
            _sSun = gl.GetUniformLocationString(_skyProgram, "uSun");
            _sSunColor = gl.GetUniformLocationString(_skyProgram, "uSunColor");
            _sZenith = gl.GetUniformLocationString(_skyProgram, "uZenith");
            _sHorizon = gl.GetUniformLocationString(_skyProgram, "uHorizon");
            _sNight = gl.GetUniformLocationString(_skyProgram, "uNight");

            _waterProgram = Link(gl, header + WaterVertexShader, header + Finish + WaterFragmentShader);
            _wVp = gl.GetUniformLocationString(_waterProgram, "uVp");
            _wArea = gl.GetUniformLocationString(_waterProgram, "uArea");
            _wEye = gl.GetUniformLocationString(_waterProgram, "uEye");
            _wLight = gl.GetUniformLocationString(_waterProgram, "uLight");
            _wLightColor = gl.GetUniformLocationString(_waterProgram, "uLightColor");
            _wZenith = gl.GetUniformLocationString(_waterProgram, "uZenith");
            _wHorizon = gl.GetUniformLocationString(_waterProgram, "uHorizon");
            _wDeep = gl.GetUniformLocationString(_waterProgram, "uDeep");
            _wFog = gl.GetUniformLocationString(_waterProgram, "uFog");

            _generateMipmap = Proc<GenerateMipmapFn>(gl, "glGenerateMipmap");
            _uniform4f = Proc<Uniform4fFn>(gl, "glUniform4f");
            _storageMs = Proc<RenderbufferStorageMultisampleFn>(gl, "glRenderbufferStorageMultisample");
            _blendFunc = Proc<BlendFuncFn>(gl, "glBlendFunc");
            _drawInstanced = Proc<DrawElementsInstancedFn>(gl, "glDrawElementsInstanced");
            _attribDivisor = Proc<VertexAttribDivisorFn>(gl, "glVertexAttribDivisor");
            _polygonOffset = Proc<PolygonOffsetFn>(gl, "glPolygonOffset");
            _stencilFunc = Proc<StencilFuncFn>(gl, "glStencilFunc");
            _stencilOp = Proc<StencilOpFn>(gl, "glStencilOp");
            _clearStencil = Proc<ClearStencilFn>(gl, "glClearStencil");
            _compressedTexImage = Proc<CompressedTexImage2DFn>(gl, "glCompressedTexImage2D");
            _texStorage = Proc<TexStorage2DFn>(gl, "glTexStorage2D");
            _compressedTexSubImage = Proc<CompressedTexSubImage2DFn>(gl, "glCompressedTexSubImage2D");
            Volatile.Write(ref _native, _compressedTexImage is null ? [] : NativeFormats(gl, es));
            _msFailed = _storageMs is null || !gl.IsBlitFramebufferAvailable;
            if (_benchLog is { } log)
            {
                try { File.AppendAllText(log, $"GL {gl.GetString(GL_VERSION)} | {gl.GetString(GL_RENDERER)} | native {string.Join(' ', _native.Keys)}{Environment.NewLine}"); }
                catch (IOException) { }
            }

            BuildGrid(gl);
            (_skyVao, _skyVbo) = BuildQuad(gl, [-1, -1, 3, -1, -1, 3]);
            (_waterVao, _waterVbo) = BuildQuad(gl, [-1, -1, 1, -1, 1, 1, -1, -1, 1, 1, -1, 1]);
            IsReady = true;
            if (_contextLost)
            {
                _contextLost = false;
                if (Meshes is not null) _pending.Enqueue((SingleKey, null, 0));
                if (_sceneKey is not null || _streaming) SceneLost?.Invoke();
            }
            else if (Meshes is not null)
            {
                _pending.Enqueue((SingleKey, null, 0));
            }
        }
        catch
        {
            IsReady = false;
            InitFailed?.Invoke();
        }
    }

    private static HashSet<string> Extensions(GlInterface gl)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (gl.GetString(GL_EXTENSIONS) is { Length: > 0 } list)
                set.UnionWith(list.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            set.Clear();
        }
        ClearErrors(gl);
        if (set.Count == 0 && Proc<GetStringiFn>(gl, "glGetStringi") is { } stringi)
        {
            gl.GetIntegerv(GL_NUM_EXTENSIONS, out int n);
            for (uint i = 0; i < n; i++)
                if (Marshal.PtrToStringAnsi(stringi(GL_EXTENSIONS, i)) is { Length: > 0 } name)
                    set.Add(name);
            ClearErrors(gl);
        }
        return set;
    }

    private Dictionary<TtpfFormat, int> NativeFormats(GlInterface gl, bool es)
    {
        var ext = Extensions(gl);
        bool Has(params string[] names) => names.Any(ext.Contains);
        int version = GlVersion.Major * 10 + GlVersion.Minor;
        bool s3tc = Has("GL_EXT_texture_compression_s3tc", "GL_WEBGL_compressed_texture_s3tc");
        var map = new Dictionary<TtpfFormat, int>();
        if (s3tc || Has("GL_EXT_texture_compression_dxt1"))
            map[TtpfFormat.Bc1] = GL_COMPRESSED_RGBA_S3TC_DXT1;
        if (s3tc || Has("GL_ANGLE_texture_compression_dxt3"))
            map[TtpfFormat.Bc2] = GL_COMPRESSED_RGBA_S3TC_DXT3;
        if (s3tc || Has("GL_ANGLE_texture_compression_dxt5"))
            map[TtpfFormat.Bc3] = GL_COMPRESSED_RGBA_S3TC_DXT5;
        if (Has("GL_EXT_texture_compression_rgtc", "GL_ARB_texture_compression_rgtc") || (!es && version >= 30))
        {
            map[TtpfFormat.Bc4] = GL_COMPRESSED_RED_RGTC1;
            map[TtpfFormat.Bc5] = GL_COMPRESSED_RG_RGTC2;
        }
        if (Has("GL_EXT_texture_compression_bptc", "GL_ARB_texture_compression_bptc") || (!es && version >= 42))
            map[TtpfFormat.Bc7] = GL_COMPRESSED_RGBA_BPTC_UNORM;
        return map;
    }

    private static void ClearErrors(GlInterface gl)
    {
        for (int i = 0; i < 16 && gl.GetError() != 0; i++) { }
    }

    private static T? Proc<T>(GlInterface gl, string name) where T : Delegate
    {
        var p = gl.GetProcAddress(name);
        return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    private static int Link(GlInterface gl, string vs, string fs)
    {
        int v = gl.CreateShader(GL_VERTEX_SHADER);
        if (gl.CompileShaderAndGetError(v, vs) is { } ve) throw new InvalidOperationException(ve);
        int f = gl.CreateShader(GL_FRAGMENT_SHADER);
        if (gl.CompileShaderAndGetError(f, fs) is { } fe) throw new InvalidOperationException(fe);
        int p = gl.CreateProgram();
        gl.AttachShader(p, v);
        gl.AttachShader(p, f);
        if (gl.LinkProgramAndGetError(p) is { } le) throw new InvalidOperationException(le);
        gl.DeleteShader(v);
        gl.DeleteShader(f);
        return p;
    }

    private unsafe void BuildGrid(GlInterface gl)
    {
        const int n = 20;
        var lines = new List<float>();
        for (int i = -n; i <= n; i++)
        {
            lines.AddRange([i, 0, -n, i, 0, n]);
            lines.AddRange([-n, 0, i, n, 0, i]);
        }
        var data = lines.ToArray();
        _gridCount = data.Length / 3;
        _gridVao = gl.GenVertexArray();
        gl.BindVertexArray(_gridVao);
        _gridVbo = gl.GenBuffer();
        gl.BindBuffer(GL_ARRAY_BUFFER, _gridVbo);
        fixed (float* p = data)
            gl.BufferData(GL_ARRAY_BUFFER, data.Length * sizeof(float), (IntPtr)p, GL_STATIC_DRAW);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, 12, IntPtr.Zero);
        gl.BindVertexArray(0);
    }

    private static unsafe (int Vao, int Vbo) BuildQuad(GlInterface gl, float[] data)
    {
        int vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);
        int vbo = gl.GenBuffer();
        gl.BindBuffer(GL_ARRAY_BUFFER, vbo);
        fixed (float* p = data)
            gl.BufferData(GL_ARRAY_BUFFER, data.Length * sizeof(float), (IntPtr)p, GL_STATIC_DRAW);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, GL_FLOAT, 0, 8, IntPtr.Zero);
        gl.BindVertexArray(0);
        return (vao, vbo);
    }

    private bool EnsureMultisample(GlInterface gl, int w, int h)
    {
        if (_msFailed || _storageMs is null) return false;
        if (_msFbo != 0 && w == _msW && h == _msH) return true;
        FreeMultisample(gl);

        _msFbo = gl.GenFramebuffer();
        gl.BindFramebuffer(GL_FRAMEBUFFER, _msFbo);
        _msColor = gl.GenRenderbuffer();
        gl.BindRenderbuffer(GL_RENDERBUFFER, _msColor);
        _storageMs(GL_RENDERBUFFER, Samples, GL_RGBA8, w, h);
        gl.FramebufferRenderbuffer(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_RENDERBUFFER, _msColor);
        _msDepth = gl.GenRenderbuffer();
        gl.BindRenderbuffer(GL_RENDERBUFFER, _msDepth);
        _storageMs(GL_RENDERBUFFER, Samples, GL_DEPTH24_STENCIL8, w, h);
        gl.FramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_RENDERBUFFER, _msDepth);
        gl.BindRenderbuffer(GL_RENDERBUFFER, 0);

        if (gl.CheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE)
        {
            FreeMultisample(gl);
            _msFailed = true;
            return false;
        }
        _msW = w;
        _msH = h;
        return true;
    }

    private void FreeMultisample(GlInterface gl)
    {
        if (_msFbo != 0) gl.DeleteFramebuffer(_msFbo);
        if (_msColor != 0) gl.DeleteRenderbuffer(_msColor);
        if (_msDepth != 0) gl.DeleteRenderbuffer(_msDepth);
        _msFbo = _msColor = _msDepth = _msW = _msH = 0;
    }

    private bool ProcessPending(GlInterface gl)
    {
        while (_pending.TryDequeue(out var op))
        {
            _work.RemoveAll(w => w.Key.Equals(op.Key));
            if (_upload is { } current && current.Key.Equals(op.Key))
            {
                FreeChunk(gl, current.Chunk);
                _upload = null;
            }
            if (_chunks.Remove(op.Key, out var old))
                FreeChunk(gl, old);
            if (ReferenceEquals(op.Key, SingleKey))
            {
                if (Meshes is { } meshes)
                    _chunks[SingleKey] = UploadMeshes(gl, meshes);
            }
            else if (op.Scene is { } scene)
                _work.Add((op.Key, scene, op.Level));
        }

        var start = _clock.Elapsed.TotalMilliseconds;
        double budget = UploadBudgetMs;
        bool finished = false;
        while (_clock.Elapsed.TotalMilliseconds - start < budget)
        {
            if (_upload is null)
            {
                if (_work.Count == 0) break;
                var (key, scene, level) = _work[0];
                _work.RemoveAt(0);
                var chunk = new Chunk { Level = level };
                _upload = new Upload(key, scene, chunk, UploadScene(gl, scene, chunk).GetEnumerator());
            }
            if (_upload.Steps.MoveNext()) continue;
            _chunks[_upload.Key] = _upload.Chunk;
            if (ReferenceEquals(_upload.Key, _sceneKey)) _uploadedScene = _upload.Scene;
            _upload = null;
            finished = true;
            _detailDirty = true;
        }
        ApplyDetail(gl, start + budget);
        double now = _clock.Elapsed.TotalSeconds;
        if (finished && _upload is null && _work.Count == 0 && now - _lastCollect > CollectInterval)
        {
            _lastCollect = now;
            ThreadPool.QueueUserWorkItem(_ => GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false));
        }
        bool busy = _upload is not null || _work.Count > 0;
        Volatile.Write(ref _backlog, _work.Count + (_upload is null ? 0 : 1));
        if (finished && !busy) UploadsDrained?.Invoke();
        return busy || !_detailDone.IsEmpty;
    }

    private int _backlog;

    public int UploadBacklog => Volatile.Read(ref _backlog) + _pending.Count;

    public event Action? UploadsDrained;

    private void FreeChunk(GlInterface gl, Chunk c)
    {
        foreach (var g in c.Groups)
        {
            foreach (var (vao, _) in g.Meshes)
                gl.DeleteVertexArray(vao);
            if (g.InstanceVbo != 0) gl.DeleteBuffer(g.InstanceVbo);
        }
        foreach (var b in c.Batches)
            FreeMesh(gl, b.Mesh);
        foreach (var m in c.OwnedMeshes)
            FreeMesh(gl, m);
        foreach (var t in c.Textures)
            ReleaseTexture(gl, t);
    }

    private static void FreeMesh(GlInterface gl, GpuMesh m)
    {
        gl.DeleteVertexArray(m.Vao);
        gl.DeleteBuffer(m.Vbo);
        gl.DeleteBuffer(m.Ibo);
    }

    private void FreeAll(GlInterface gl)
    {
        if (_upload is { } current)
            FreeChunk(gl, current.Chunk);
        _upload = null;
        _work.Clear();
        foreach (var c in _chunks.Values)
            FreeChunk(gl, c);
        _chunks.Clear();
        foreach (var entry in _textures.Values)
        {
            gl.DeleteTexture(entry.Name);
            entry.Dead = true;
        }
        _textures.Clear();
        _streamed.Clear();
        _textureBytes = _detailBytes = 0;
    }

    private GpuTexture AcquireTexture(GlInterface gl, Chunk chunk, DecodedTexture t)
    {
        object key = t.Key != 0 ? (t.Key, t.Width) : new TextureIdentity(t.Rgba);
        chunk.Textures.Add(key);
        if (_textures.TryGetValue(key, out var entry))
        {
            entry.Refs++;
            return entry;
        }
        entry = new GpuTexture(key) { Refs = 1, Base = Math.Max(t.Width, t.Height) };
        entry.Size = entry.Base;
        var (name, bytes) = t.Blocks is { } blocks ? CreateTexture(gl, blocks) : (0, 0L);
        if (name == 0)
            (name, bytes) = CreateTexture(gl, new MipChain { Format = TtpfFormat.Rgba8, Width = t.Width, Height = t.Height, Levels = [t.Rgba] });
        entry.Name = name;
        entry.Bytes = bytes;
        _textureBytes += bytes;
        _textures[key] = entry;
        if (t.Key != 0 && TextureSource is not null)
        {
            entry.Hash = t.Key;
            _streamed.Add(entry);
        }
        return entry;
    }

    private unsafe (int Name, long Bytes) CreateTexture(GlInterface gl, MipChain chain)
    {
        bool native = chain.Format != TtpfFormat.Rgba8;
        int internalFormat = 0;
        if (native && (_compressedTexImage is null || !_native.TryGetValue(chain.Format, out internalFormat)))
            return (0, 0);
        ClearErrors(gl);
        int tex = gl.GenTexture();
        gl.BindTexture(GL_TEXTURE_2D, tex);
        long bytes = 0;
        bool mipmapped;
        if (native)
        {
            bool immutable = _texStorage is not null && _compressedTexSubImage is not null;
            if (immutable)
                _texStorage!(GL_TEXTURE_2D, chain.Levels.Length, internalFormat, (int)chain.Width, (int)chain.Height);
            for (int level = 0; level < chain.Levels.Length; level++)
            {
                var data = chain.Levels[level];
                int lw = (int)Math.Max(chain.Width >> level, 1), lh = (int)Math.Max(chain.Height >> level, 1);
                fixed (byte* pd = data)
                {
                    if (immutable)
                        _compressedTexSubImage!(GL_TEXTURE_2D, level, 0, 0, lw, lh, internalFormat, data.Length, (IntPtr)pd);
                    else
                        _compressedTexImage!(GL_TEXTURE_2D, level, internalFormat, lw, lh, 0, data.Length, (IntPtr)pd);
                }
                bytes += data.Length;
            }
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAX_LEVEL, chain.Levels.Length - 1);
            mipmapped = chain.Levels.Length > 1;
            if (gl.GetError() != 0)
            {
                gl.DeleteTexture(tex);
                var reduced = new Dictionary<TtpfFormat, int>(_native);
                reduced.Remove(chain.Format);
                Volatile.Write(ref _native, reduced);
                return (0, 0);
            }
        }
        else
        {
            fixed (byte* pt = chain.Levels[0])
                gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, (int)chain.Width, (int)chain.Height, 0, GL_RGBA, GL_UNSIGNED_BYTE, (IntPtr)pt);
            bytes = chain.Levels[0].LongLength;
            mipmapped = _generateMipmap is not null;
            if (_generateMipmap is { } gen)
            {
                gen(GL_TEXTURE_2D);
                bytes = bytes * 4 / 3;
            }
        }
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, mipmapped ? GL_LINEAR_MIPMAP_LINEAR : GL_LINEAR);
        return (tex, bytes);
    }

    private static uint DetailFor(Vector3 eye, Vector3 center, float radius)
    {
        if (!float.IsFinite(radius)) return 0;
        float d = MathF.Max(Vector3.Distance(eye, center) - radius, 1f);
        if (d > DetailRange) return 0;
        float texels = MathF.Min(DetailScale / d, DetailMax);
        return texels < 1f ? 0 : 1u << (int)MathF.Log2(texels);
    }

    private static void Want(GpuMesh m, uint size)
    {
        if (size == 0) return;
        Want(m.Texture, size);
        Want(m.Normal, size);
        Want(m.Mask, size);
        foreach (var l in m.Layers)
            Want(l, size);
    }

    private static void Want(GpuTexture? t, uint size)
    {
        if (t is not null && t.Hash != 0 && size > t.Wanted) t.Wanted = size;
    }

    private void UpdateDetail(Vector3 eye, double now, bool moving)
    {
        if (TextureSource is null || _streamed.Count == 0)
        {
            _detailDirty = false;
            return;
        }
        bool due = now - _detailAt >= DetailInterval;
        bool settled = !moving && (_detailDirty || eye != _detailEye) && now - _detailAt >= DetailSettle;
        if (!due && !settled) return;
        _detailAt = now;
        _detailEye = eye;
        _detailDirty = false;

        foreach (var t in _streamed)
            t.Wanted = 0;
        foreach (var chunk in _chunks.Values)
        {
            foreach (var b in chunk.Batches)
                Want(b.Mesh, DetailFor(eye, b.Center, b.Radius));
            foreach (var g in chunk.Groups)
            {
                uint size = DetailFor(eye, g.Center, g.Radius);
                if (size == 0) continue;
                foreach (var (_, m) in g.Meshes)
                    Want(m, size);
            }
        }

        var upgrades = new List<GpuTexture>();
        var downgrades = new List<GpuTexture>();
        foreach (var t in _streamed)
        {
            uint target = Math.Min(Math.Max(t.Wanted, t.Base), t.Ceiling);
            if (target >= t.Size) t.LastWanted = now;
            if (t.Pending != 0) continue;
            if (target > t.Size) upgrades.Add(t);
            else if (t.Size > t.Base && target < t.Size) downgrades.Add(t);
        }

        long projected = _detailBytes;
        var evictable = projected + (DetailBudget >> 4) > DetailBudget
            ? _streamed.Where(t => t.Pending == 0 && t.Size > t.Base).OrderBy(t => t.Wanted).ThenBy(t => t.LastWanted).ToList()
            : [];
        int evicted = 0;
        while (projected > DetailBudget && evicted < evictable.Count && _detailInFlight < DetailInFlight * 2)
        {
            var t = evictable[evicted++];
            projected -= t.Bytes;
            Request(t, t.Base);
        }

        foreach (var t in upgrades.OrderByDescending(t => t.Wanted).ThenBy(t => t.Size))
        {
            if (_detailInFlight >= DetailInFlight) break;
            uint target = Math.Min(Math.Max(t.Wanted, t.Base), t.Ceiling);
            long estimate = t.Bytes * ((long)target * target) / Math.Max(1L, (long)t.Size * t.Size);
            while (projected + estimate > DetailBudget && evicted < evictable.Count && evictable[evicted].Wanted < t.Wanted)
            {
                var victim = evictable[evicted++];
                if (victim.Pending != 0) continue;
                projected -= victim.Bytes;
                Request(victim, victim.Base);
            }
            if (projected + estimate > DetailBudget) break;
            projected += estimate;
            Request(t, target);
        }

        foreach (var t in downgrades)
        {
            if (_detailInFlight >= DetailInFlight) break;
            if (now - t.LastWanted < DetailLinger) continue;
            Request(t, Math.Max(t.Wanted, t.Base));
        }
    }

    private void Request(GpuTexture t, uint size)
    {
        if (t.Pending != 0 || size == t.Size) return;
        t.Pending = size;
        _detailInFlight++;
        _detailWorker ??= Task.Run(DetailWorker);
        _detailQueue.Writer.TryWrite(new DetailRequest(t, size));
    }

    private async Task DetailWorker()
    {
        await foreach (var r in _detailQueue.Reader.ReadAllAsync())
        {
            MipChain? chain = null;
            if (!r.Texture.Dead && TextureSource is { } source)
            {
                try { chain = source(r.Texture.Hash, r.Size); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException) { chain = null; }
            }
            _detailDone.Enqueue(new DetailResult(r.Texture, r.Size, chain));
            Avalonia.Threading.Dispatcher.UIThread.Post(RequestNextFrameRendering);
        }
    }

    private void ApplyDetail(GlInterface gl, double deadline)
    {
        bool first = true;
        while ((first || _clock.Elapsed.TotalMilliseconds < deadline) && _detailDone.TryDequeue(out var r))
        {
            _detailInFlight--;
            var t = r.Texture;
            t.Pending = 0;
            _detailDirty = true;
            if (t.Dead) continue;
            if (r.Chain is not { } chain)
            {
                if (r.Size > t.Size) t.Ceiling = t.Size;
                continue;
            }
            uint got = Math.Max(chain.Width, chain.Height);
            if (r.Size > t.Size && got <= t.Size)
            {
                t.Ceiling = t.Size;
                continue;
            }
            if (got < r.Size) t.Ceiling = got;
            if (got == t.Size) continue;
            var (name, bytes) = CreateTexture(gl, chain);
            if (name == 0) continue;
            first = false;
            _detailDirty = true;
            gl.DeleteTexture(t.Name);
            _textureBytes += bytes - t.Bytes;
            if (t.Size > t.Base) _detailBytes -= t.Bytes;
            t.Name = name;
            t.Bytes = bytes;
            t.Size = got;
            if (t.Size > t.Base) _detailBytes += t.Bytes;
        }
    }

    private sealed class TextureIdentity(byte[] rgba)
    {
        private readonly byte[] _rgba = rgba;

        public override bool Equals(object? obj) => obj is TextureIdentity other && ReferenceEquals(other._rgba, _rgba);

        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_rgba);
    }

    private void ReleaseTexture(GlInterface gl, object key)
    {
        if (!_textures.TryGetValue(key, out var entry)) return;
        if (--entry.Refs > 0) return;
        gl.DeleteTexture(entry.Name);
        entry.Dead = true;
        _textureBytes -= entry.Bytes;
        if (entry.Size > entry.Base) _detailBytes -= entry.Bytes;
        _textures.Remove(key);
        _streamed.Remove(entry);
    }

    private unsafe GpuMesh UploadGeometry(GlInterface gl, Chunk chunk, float[] vertices, uint[] indices,
        DecodedTexture? texture, DecodedTexture? normal, bool cutout, bool blend, DecodedTexture? mask = null, DecodedTexture?[]? layers = null)
    {
        int vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);
        int vbo = gl.GenBuffer();
        gl.BindBuffer(GL_ARRAY_BUFFER, vbo);
        fixed (float* pv = vertices)
            gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)((long)vertices.Length * sizeof(float)), (IntPtr)pv, GL_STATIC_DRAW);
        int ibo = gl.GenBuffer();
        gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, ibo);
        bool shortIndices = vertices.Length / SceneBatcher.Stride <= ushort.MaxValue + 1;
        if (shortIndices)
        {
            var narrow = new ushort[indices.Length];
            for (int k = 0; k < indices.Length; k++) narrow[k] = (ushort)indices[k];
            fixed (ushort* ps = narrow)
                gl.BufferData(GL_ELEMENT_ARRAY_BUFFER, (IntPtr)((long)narrow.Length * sizeof(ushort)), (IntPtr)ps, GL_STATIC_DRAW);
        }
        else
        {
            fixed (uint* pi = indices)
                gl.BufferData(GL_ELEMENT_ARRAY_BUFFER, (IntPtr)((long)indices.Length * sizeof(uint)), (IntPtr)pi, GL_STATIC_DRAW);
        }
        BindVertexLayout(gl);
        gl.BindVertexArray(0);
        var tex = texture is { } t ? AcquireTexture(gl, chunk, t) : null;
        var nrm = normal is { } n ? AcquireTexture(gl, chunk, n) : null;
        var maskName = mask is { } mk && layers is { Length: > 0 } ? AcquireTexture(gl, chunk, mk) : null;
        GpuTexture[] layerNames = maskName is null || layers is null
            ? []
            : [.. layers.Where(l => l is not null).Take(4).Select(l => AcquireTexture(gl, chunk, l!.Value))];
        return new GpuMesh(vao, vbo, ibo, indices.Length, tex, nrm, cutout, blend) { Mask = maskName, Layers = layerNames, IndexType = shortIndices ? GL_UNSIGNED_SHORT : GL_UNSIGNED_INT };
    }

    private static void BindVertexLayout(GlInterface gl)
    {
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, Stride, IntPtr.Zero);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 4, GL_BYTE, 1, Stride, (IntPtr)12);
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 2, GL_FLOAT, 0, Stride, (IntPtr)16);
        gl.EnableVertexAttribArray(7);
        gl.VertexAttribPointer(7, 2, GL_FLOAT, 0, Stride, (IntPtr)24);
        gl.EnableVertexAttribArray(8);
        gl.VertexAttribPointer(8, 4, GL_UNSIGNED_BYTE, 1, Stride, (IntPtr)32);
    }

    private GpuMesh UploadBatch(GlInterface gl, Chunk chunk, SceneBatch b) =>
        UploadGeometry(gl, chunk, b.Vertices, b.Indices, b.Texture, b.Normal, b.Cutout, b.Blend, b.Mask, b.Layers);

    private IEnumerable<bool> UploadScene(GlInterface gl, Scene scene, Chunk chunk)
    {
        foreach (var b in scene.Batches)
        {
            if (b.Indices.Length == 0) continue;
            chunk.Batches.Add(new GpuBatch(UploadBatch(gl, chunk, b), ToYUp(b.Center), b.Radius, b.Small, b.Triangles));
            b.Release();
            yield return true;
        }

        var models = new Dictionary<(int Model, int Tint), List<GpuMesh>>();
        foreach (var group in scene.InstanceGroups)
        {
            var variant = (group.Model, group.Tint);
            if (!models.TryGetValue(variant, out var meshes))
            {
                meshes = [];
                if (scene.ModelBatches.TryGetValue(variant, out var merged))
                    foreach (var b in merged)
                        if (b.Indices.Length > 0)
                            meshes.Add(UploadBatch(gl, chunk, b));
                models[variant] = meshes;
                chunk.OwnedMeshes.AddRange(meshes);
            }
            chunk.Groups.Add(UploadGroup(gl, group, meshes));
            yield return true;
        }
        foreach (var list in scene.ModelBatches.Values)
            foreach (var b in list)
                b.Release();
    }

    private unsafe GpuGroup UploadGroup(GlInterface gl, InstanceGroup group, List<GpuMesh> meshes)
    {
        int instanceVbo = gl.GenBuffer();
        gl.BindBuffer(GL_ARRAY_BUFFER, instanceVbo);
        fixed (Matrix4x4* pm = group.Transforms)
            gl.BufferData(GL_ARRAY_BUFFER, (IntPtr)((long)group.Transforms.Length * 64), (IntPtr)pm, GL_STATIC_DRAW);
        var vaos = new List<(int Vao, GpuMesh Mesh)>(meshes.Count);
        foreach (var mesh in meshes)
        {
            int vao = gl.GenVertexArray();
            gl.BindVertexArray(vao);
            gl.BindBuffer(GL_ARRAY_BUFFER, mesh.Vbo);
            BindVertexLayout(gl);
            gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, mesh.Ibo);
            gl.BindBuffer(GL_ARRAY_BUFFER, instanceVbo);
            for (int k = 0; k < 4; k++)
            {
                gl.EnableVertexAttribArray(3 + k);
                gl.VertexAttribPointer(3 + k, 4, GL_FLOAT, 0, 64, (IntPtr)(k * 16));
                _attribDivisor?.Invoke(3 + k, 1);
            }
            gl.BindVertexArray(0);
            vaos.Add((vao, mesh));
        }
        return new GpuGroup(vaos, instanceVbo, group.Transforms.Length, ToYUp(group.Center), group.Radius, group.Small);
    }

    private Chunk UploadMeshes(GlInterface gl, IReadOnlyList<DecodedMesh> meshes)
    {
        var chunk = new Chunk();
        foreach (var m in meshes)
        {
            int vc = m.Positions.Count;
            if (vc == 0 || m.Indices.Count < 3) continue;
            var uv = m.Uvs.Count > 0 && m.Uvs[0].Uv.Count == vc ? m.Uvs[0].Uv : null;
            var terrain = uv is not null && m.Terrain is { LayerUv: { } lu } t && lu.Count == vc ? t : null;
            var layerUv = terrain?.LayerUv;
            if (m.Pass == MaterialPass.Decal && m.Texture is null && terrain is null) continue;
            m.KeepVertexAlpha();
            m.KeepVertexTint();
            var alpha = m.VertexAlpha is { } va && va.Length == vc ? va : null;
            var tints = m.VertexTint is { } vt && vt.Length == 4 * vc ? vt : null;
            var normals = MeshNormals.Compute(m);
            var verts = new float[vc * SceneBatcher.Stride];
            for (int i = 0; i < vc; i++)
            {
                int o = i * SceneBatcher.Stride;
                verts[o] = m.Positions[i].X;
                verts[o + 1] = m.Positions[i].Y;
                verts[o + 2] = m.Positions[i].Z;
                verts[o + 3] = SceneBatcher.PackNormal(normals[i], alpha is null ? (byte)255 : alpha[i]);
                if (uv is not null)
                {
                    verts[o + 4] = uv[i].U;
                    verts[o + 5] = uv[i].V;
                }
                if (layerUv is not null)
                {
                    verts[o + 6] = layerUv[i].U;
                    verts[o + 7] = layerUv[i].V;
                }
                verts[o + 8] = tints is null ? SceneBatcher.White : BitConverter.UInt32BitsToSingle(BitConverter.ToUInt32(tints, 4 * i));
            }
            var tex = uv is not null && terrain is null ? m.Texture : null;
            var nrm = uv is not null ? m.NormalMap : null;
            bool blend = tex is not null && !m.TextureCutout && m.Pass is MaterialPass.Alpha or MaterialPass.Decal;
            var mesh = UploadGeometry(gl, chunk, verts, [.. m.Indices], tex, nrm, m.TextureCutout, blend, terrain?.Mask, terrain?.Layers);
            chunk.Batches.Add(new GpuBatch(mesh, Vector3.Zero, float.PositiveInfinity, false, m.Indices.Count / 3));
        }
        return chunk;
    }

    private static Vector3 ToYUp(Vector3 zUp) => new(zUp.X, zUp.Z, -zUp.Y);

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!IsReady) return;
        bool morePending = ProcessPending(gl);

        _frameWatch.Restart();
        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _lastFrame, 0, 0.1);
        _lastFrame = now;
        bool moving = ApplyMovement(dt);
        if (_benchLog is not null && _benchFly && _streaming)
        {
            _pan.X += dt * 40f;
            moving = true;
        }
        else if (_benchLog is not null && _benchFly && _chunks.Count > 0)
        {
            _yaw += dt * 0.35f;
            moving = true;
        }

        double scale = Avalonia.Controls.TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int w = Math.Max(1, (int)(Bounds.Width * scale));
        int h = Math.Max(1, (int)(Bounds.Height * scale));

        bool ms = EnsureMultisample(gl, w, h);
        gl.BindFramebuffer(GL_FRAMEBUFFER, ms ? _msFbo : fb);
        gl.Viewport(0, 0, w, h);
        gl.ClearColor(0.11f, 0.11f, 0.125f, 1f);
        gl.ClearDepth(1f);
        _clearStencil?.Invoke(0);
        gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT | (ms ? GL_STENCIL_BUFFER_BIT : 0));
        _layered = ms && _stencilFunc is not null && _stencilOp is not null;
        gl.Enable(GL_DEPTH_TEST);
        gl.DepthFunc(GL_LEQUAL);

        var target = _center + _pan;
        var eye = target + new Vector3(
            MathF.Cos(_pitch) * MathF.Sin(_yaw),
            MathF.Sin(_pitch),
            MathF.Cos(_pitch) * MathF.Cos(_yaw)) * _distance;
        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        float near = MathF.Max(_distance * 0.005f, 0.05f);
        float far = _streaming ? _distance + MathF.Max(7000f, _viewRadius * 1.2f) : _distance + _radius * 4f;
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(0.8f, w / (float)h, near, far);
        var model = new Matrix4x4(
            1, 0, 0, 0,
            0, 0, -1, 0,
            0, 1, 0, 0,
            0, 0, 0, 1);
        var vp = view * proj;
        var mvp = model * vp;

        bool world = _streaming || Scene is not null;
        var atmosphere = world ? Atmosphere.At(TimeOfDay) : default;
        if (world)
            DrawSky(gl, vp, eye, atmosphere);
        else
        {
            float cell = MathF.Pow(10, MathF.Floor(MathF.Log10(_radius * 2f))) / 4f;
            var gridModel = Matrix4x4.CreateScale(cell) * Matrix4x4.CreateTranslation(_center.X, _center.Y - _radius, _center.Z);
            var gridMvp = gridModel * vp;
            gl.UseProgram(_gridProgram);
            gl.UniformMatrix4fv(_gMvp, 1, false, &gridMvp);
            gl.BindVertexArray(_gridVao);
            gl.DrawArrays(GL_LINES, 0, (IntPtr)_gridCount);
        }

        gl.UseProgram(_program);
        gl.UniformMatrix4fv(_uMvp, 1, false, &mvp);
        gl.UniformMatrix4fv(_uModel, 1, false, &model);
        gl.Uniform1i(_uTex, 0);
        gl.Uniform1i(_uNormalTex, 1);
        gl.Uniform1i(_uMask, 2);
        for (int k = 0; k < _uLayer.Length; k++)
            gl.Uniform1i(_uLayer[k], 3 + k);
        gl.Uniform1i(_uLayers, 0);
        gl.Uniform1i(_uInstanced, 0);
        _uniform4f?.Invoke(_uEye, eye.X, eye.Y, eye.Z, 1);
        if (world)
        {
            SetVec(_uLight, atmosphere.Light, 0);
            SetVec(_uLightColor, atmosphere.LightColor, 0);
            SetVec(_uFill, Vector3.UnitY, 0);
            SetVec(_uSkyAmbient, atmosphere.SkyAmbient, 0);
            SetVec(_uGroundAmbient, atmosphere.GroundAmbient, 0);
            SetVec(_uFog, atmosphere.Horizon, FogDensity(atmosphere));
        }
        else
        {
            var toEye = Vector3.Normalize(eye - target);
            var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, toEye));
            var light = Vector3.Normalize(toEye + side * 0.6f + Vector3.UnitY * 0.8f);
            SetVec(_uLight, light, 0);
            SetVec(_uLightColor, new Vector3(0.95f, 0.91f, 0.86f), 0);
            SetVec(_uFill, Vector3.Normalize(new Vector3(-light.X, 0.35f, -light.Z)), 0.35f);
            SetVec(_uSkyAmbient, new Vector3(0.30f, 0.33f, 0.38f), 0);
            SetVec(_uGroundAmbient, new Vector3(0.16f, 0.15f, 0.14f), 0);
            SetVec(_uFog, Vector3.Zero, 0);
        }

        UpdateDetail(eye, now, moving);
        var planes = FrustumPlanes(vp);
        DrawChunks(gl, planes, eye, blended: false);
        if (world && ShowWater && (_streaming || _aerial))
        {
            DrawWater(gl, vp, eye, far, atmosphere);
            gl.UseProgram(_program);
        }
        DrawChunks(gl, planes, eye, blended: true);
        gl.BindVertexArray(0);
        gl.ActiveTexture(GL_TEXTURE0);

        if (ms)
        {
            gl.BindFramebuffer(GL_READ_FRAMEBUFFER, _msFbo);
            gl.BindFramebuffer(GL_DRAW_FRAMEBUFFER, fb);
            gl.BlitFramebuffer(0, 0, w, h, 0, 0, w, h, GL_COLOR_BUFFER_BIT, GL_NEAREST);
            gl.BindFramebuffer(GL_FRAMEBUFFER, fb);
        }

        ReportCamera(eye, target);
        if (moving || morePending || _detailDirty)
            RequestNextFrameRendering();
        ReportStats(now);
    }

    public Vector3 LookTarget { get; private set; }

    private void ReportCamera(Vector3 yUpEye, Vector3 yUpTarget)
    {
        LookTarget = new Vector3(yUpTarget.X, -yUpTarget.Z, yUpTarget.Y);
        if (!_streaming) return;
        float height = MathF.Max(0f, yUpEye.Y - _groundY);
        var state = new Vector3(yUpEye.X, height, yUpEye.Z);
        if (float.IsFinite(_reportedEye.X) && Vector3.Distance(_reportedEye, state) < MathF.Max(10f, height * 0.05f)) return;
        _reportedEye = state;
        CameraMoved?.Invoke(new Vector3(yUpEye.X, -yUpEye.Z, yUpEye.Y), height);
    }

    private void ReportStats(double now)
    {
        _frameMs = _frameMs * 0.9 + _frameWatch.Elapsed.TotalMilliseconds * 0.1;
        _frames++;
        if (now - _statsAt < 0.5) return;
        double fps = _frames / Math.Max(1e-3, now - _statsAt);
        _statsAt = now;
        _frames = 0;
        string text = $"{_frameMs:F1} ms CPU    {fps:F0} fps    {_drawCalls:N0} draws    {_triangles / 1_000_000.0:F2} M triangles    {_chunks.Count} chunks    {_textureBytes >> 20} MB textures ({_detailBytes >> 20} MB sharp)    {GC.GetTotalMemory(false) >> 20} MB managed";
        StatsUpdated?.Invoke(text);
        if (_benchLog is { } log)
        {
            try { File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss} {text}{Environment.NewLine}"); }
            catch (IOException) { }
        }
    }

    private struct DrawState
    {
        public int Texture, Normal, HasTexture, HasNormal, Cutout, Mask, Layers;
        public int Layer0, Layer1, Layer2, Layer3;
    }

    private void SetVec(int location, Vector3 v, float w) => _uniform4f?.Invoke(location, v.X, v.Y, v.Z, w);

    private float FogDensity(Atmosphere a) => a.FogDensity * MathF.Min(1f, 3000f / MathF.Max(_viewRadius, 1f));

    private unsafe void DrawSky(GlInterface gl, Matrix4x4 vp, Vector3 eye, Atmosphere a)
    {
        if (!Matrix4x4.Invert(vp, out var inv)) return;
        gl.Disable(GL_DEPTH_TEST);
        gl.DepthMask(0);
        gl.UseProgram(_skyProgram);
        gl.UniformMatrix4fv(_sInvVp, 1, false, &inv);
        _uniform4f?.Invoke(_sEye, eye.X, eye.Y, eye.Z, 1);
        _uniform4f?.Invoke(_sSun, a.Sun.X, a.Sun.Y, a.Sun.Z, 0);
        _uniform4f?.Invoke(_sSunColor, a.SunColor.X, a.SunColor.Y, a.SunColor.Z, 0);
        _uniform4f?.Invoke(_sZenith, a.Zenith.X, a.Zenith.Y, a.Zenith.Z, 0);
        _uniform4f?.Invoke(_sHorizon, a.Horizon.X, a.Horizon.Y, a.Horizon.Z, 0);
        _uniform4f?.Invoke(_sNight, a.Night, 0, 0, 0);
        gl.BindVertexArray(_skyVao);
        gl.DrawArrays(GL_TRIANGLES, 0, (IntPtr)3);
        gl.DepthMask(1);
        gl.Enable(GL_DEPTH_TEST);
    }

    private unsafe void DrawWater(GlInterface gl, Matrix4x4 vp, Vector3 eye, float far, Atmosphere a)
    {
        if (_blendFunc is not { } blendFunc) return;
        gl.UseProgram(_waterProgram);
        gl.UniformMatrix4fv(_wVp, 1, false, &vp);
        _uniform4f?.Invoke(_wArea, eye.X, WaterLevel, eye.Z, far * 0.98f);
        _uniform4f?.Invoke(_wEye, eye.X, eye.Y, eye.Z, 1);
        _uniform4f?.Invoke(_wLight, a.Light.X, a.Light.Y, a.Light.Z, 0);
        _uniform4f?.Invoke(_wLightColor, a.LightColor.X, a.LightColor.Y, a.LightColor.Z, 0);
        _uniform4f?.Invoke(_wZenith, a.Zenith.X, a.Zenith.Y, a.Zenith.Z, 0);
        _uniform4f?.Invoke(_wHorizon, a.Horizon.X, a.Horizon.Y, a.Horizon.Z, 0);
        _uniform4f?.Invoke(_wDeep, a.WaterDeep.X, a.WaterDeep.Y, a.WaterDeep.Z, 0);
        _uniform4f?.Invoke(_wFog, a.Horizon.X, a.Horizon.Y, a.Horizon.Z, FogDensity(a));
        gl.Enable(GL_BLEND);
        blendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        gl.BindVertexArray(_waterVao);
        gl.DrawArrays(GL_TRIANGLES, 0, (IntPtr)6);
        gl.Disable(GL_BLEND);
    }

    private void DrawChunks(GlInterface gl, Vector4[] planes, Vector3 eye, bool blended)
    {
        if (!blended)
        {
            _drawCalls = 0;
            _triangles = 0;
        }
        {
            if (blended)
            {
                if (_blendFunc is not { } blendFunc) return;
                gl.Enable(GL_BLEND);
                blendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
                gl.DepthMask(0);
                if (_polygonOffset is { } offset)
                {
                    gl.Enable(GL_POLYGON_OFFSET_FILL);
                    offset(-1f, -2f);
                }
            }
            gl.Uniform1i(_uBlend, blended ? 1 : 0);
            var state = new DrawState
            {
                Texture = -1, Normal = -1, HasTexture = -1, HasNormal = -1, Cutout = -1, Mask = -1, Layers = -1,
                Layer0 = -1, Layer1 = -1, Layer2 = -1, Layer3 = -1,
            };

            if (_layered) gl.Enable(GL_STENCIL_TEST);
            for (int level = 0; level <= MaxDetailLevel; level++)
            {
                if (_layered)
                {
                    int reference = MaxDetailLevel + 1 - level;
                    _stencilFunc!(GL_GEQUAL, reference, 0xff);
                    _stencilOp!(GL_KEEP, GL_KEEP, blended ? GL_KEEP : GL_REPLACE);
                }
                foreach (var chunk in _chunks.Values)
                {
                    if (chunk.Level != level) continue;
                    foreach (var b in chunk.Batches)
                    {
                        if (b.Mesh.Blend != blended || !InView(planes, eye, b.Center, b.Radius, b.Small)) continue;
                        Bind(gl, b.Mesh, ref state);
                        gl.BindVertexArray(b.Mesh.Vao);
                        gl.DrawElements(GL_TRIANGLES, b.Mesh.Count, b.Mesh.IndexType, IntPtr.Zero);
                        _drawCalls++;
                        _triangles += b.Triangles;
                    }
                }

                if (_drawInstanced is not { } drawInstanced) continue;
                gl.Uniform1i(_uInstanced, 1);
                foreach (var chunk in _chunks.Values)
                {
                    if (chunk.Level != level) continue;
                    foreach (var g in chunk.Groups)
                    {
                        if (!InView(planes, eye, g.Center, g.Radius, g.Small)) continue;
                        foreach (var (vao, m) in g.Meshes)
                        {
                            if (m.Blend != blended) continue;
                            Bind(gl, m, ref state);
                            gl.BindVertexArray(vao);
                            drawInstanced(GL_TRIANGLES, m.Count, m.IndexType, IntPtr.Zero, g.Count);
                            _drawCalls++;
                            _triangles += m.Count / 3 * g.Count;
                        }
                    }
                }
                gl.Uniform1i(_uInstanced, 0);
            }
            if (_layered) gl.Disable(GL_STENCIL_TEST);

            if (blended)
            {
                gl.Uniform1i(_uBlend, 0);
                gl.Disable(GL_POLYGON_OFFSET_FILL);
                gl.DepthMask(1);
                gl.Disable(GL_BLEND);
            }
        }
    }

    private static bool InView(Vector4[] planes, Vector3 eye, Vector3 c, float radius, bool small)
    {
        if (float.IsInfinity(radius)) return true;
        if (small && Vector3.Distance(c, eye) - radius > SmallDistance) return false;
        foreach (var pl in planes)
            if (pl.X * c.X + pl.Y * c.Y + pl.Z * c.Z + pl.W < -radius)
                return false;
        return true;
    }

    private void Bind(GlInterface gl, GpuMesh m, ref DrawState s)
    {
        int texture = m.Texture?.Name ?? 0, normal = m.Normal?.Name ?? 0;
        int ht = texture != 0 ? 1 : 0, hn = normal != 0 ? 1 : 0, ct = m.Cutout ? 1 : 0;
        if (ht != s.HasTexture) { gl.Uniform1i(_uHasTex, ht); s.HasTexture = ht; }
        if (hn != s.HasNormal) { gl.Uniform1i(_uHasNormal, hn); s.HasNormal = hn; }
        if (ct != s.Cutout) { gl.Uniform1i(_uCutout, ct); s.Cutout = ct; }
        if (texture != s.Texture)
        {
            gl.ActiveTexture(GL_TEXTURE0);
            gl.BindTexture(GL_TEXTURE_2D, texture);
            s.Texture = texture;
        }
        if (normal != s.Normal)
        {
            gl.ActiveTexture(GL_TEXTURE1);
            gl.BindTexture(GL_TEXTURE_2D, normal);
            s.Normal = normal;
        }
        int layers = m.Mask is not null ? m.Layers.Length : 0;
        if (layers != s.Layers) { gl.Uniform1i(_uLayers, layers); s.Layers = layers; }
        if (layers == 0) return;
        BindUnit(gl, 2, m.Mask!.Name, ref s.Mask);
        BindUnit(gl, 3, m.Layers[0].Name, ref s.Layer0);
        if (layers > 1) BindUnit(gl, 4, m.Layers[1].Name, ref s.Layer1);
        if (layers > 2) BindUnit(gl, 5, m.Layers[2].Name, ref s.Layer2);
        if (layers > 3) BindUnit(gl, 6, m.Layers[3].Name, ref s.Layer3);
    }

    private static void BindUnit(GlInterface gl, int unit, int texture, ref int bound)
    {
        if (texture == bound) return;
        gl.ActiveTexture(GL_TEXTURE0 + unit);
        gl.BindTexture(GL_TEXTURE_2D, texture);
        bound = texture;
    }

    private static Vector4[] FrustumPlanes(Matrix4x4 m)
    {
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        Vector4[] planes = [c4 + c1, c4 - c1, c4 + c2, c4 - c2, c3, c4 - c3];
        for (int i = 0; i < planes.Length; i++)
        {
            float len = new Vector3(planes[i].X, planes[i].Y, planes[i].Z).Length();
            if (len > 0) planes[i] /= len;
        }
        return planes;
    }

    private bool ApplyMovement(float dt)
    {
        if (_keys.Count == 0) return false;
        var forward = -new Vector3(MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch), MathF.Cos(_pitch) * MathF.Cos(_yaw));
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var move = Vector3.Zero;
        if (_keys.Contains(Key.W) || _keys.Contains(Key.Z) || _keys.Contains(Key.Up)) move += forward;
        if (_keys.Contains(Key.S) || _keys.Contains(Key.Down)) move -= forward;
        if (_keys.Contains(Key.D) || _keys.Contains(Key.Right)) move += right;
        if (_keys.Contains(Key.A) || _keys.Contains(Key.Q) || _keys.Contains(Key.Left)) move -= right;
        if (_keys.Contains(Key.E) || _keys.Contains(Key.Space)) move += Vector3.UnitY;
        if (_keys.Contains(Key.C)) move -= Vector3.UnitY;
        if (move == Vector3.Zero) return false;
        float baseSpeed = _streaming ? 2.5f * Math.Clamp(_distance, 2f, 40f) : MathF.Max(_radius * 0.35f, 2f);
        bool fast = _keys.Contains(Key.LeftShift) || _keys.Contains(Key.RightShift);
        bool slow = _keys.Contains(Key.LeftCtrl) || _keys.Contains(Key.RightCtrl);
        float speed = baseSpeed * (fast ? 4f : 1f) * (slow ? 0.25f : 1f);
        _pan += Vector3.Normalize(move) * speed * dt;
        return true;
    }

    private static bool IsMovementKey(Key k) => k is Key.W or Key.Z or Key.S or Key.A or Key.Q or Key.D or Key.E or Key.C
        or Key.Space or Key.Up or Key.Down or Key.Left or Key.Right or Key.LeftShift or Key.RightShift;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            _keys.Add(e.Key);
            return;
        }
        bool shortcut = e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.S or Key.E;
        if (!IsMovementKey(e.Key) || shortcut || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            return;
        if (_keys.Add(e.Key))
        {
            _lastFrame = _clock.Elapsed.TotalSeconds;
            RequestNextFrameRendering();
        }
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _keys.Remove(e.Key);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        _keys.Clear();
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        FreeAll(gl);
        while (_pending.TryDequeue(out _)) { }
        FreeMultisample(gl);
        if (_gridVao != 0) gl.DeleteVertexArray(_gridVao);
        if (_gridVbo != 0) gl.DeleteBuffer(_gridVbo);
        if (_program != 0) gl.DeleteProgram(_program);
        if (_gridProgram != 0) gl.DeleteProgram(_gridProgram);
        foreach (var vao in new[] { _skyVao, _waterVao })
            if (vao != 0) gl.DeleteVertexArray(vao);
        foreach (var vbo in new[] { _skyVbo, _waterVbo })
            if (vbo != 0) gl.DeleteBuffer(vbo);
        foreach (var program in new[] { _skyProgram, _waterProgram })
            if (program != 0) gl.DeleteProgram(program);
        _gridVao = _gridVbo = _program = _gridProgram = 0;
        _skyVao = _skyVbo = _waterVao = _waterVbo = _skyProgram = _waterProgram = 0;
        _contextLost = true;
        IsReady = false;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        _panning = props.IsRightButtonPressed || props.IsMiddleButtonPressed;
        _last = e.GetPosition(this);
        e.Pointer.Capture(this);
        if (e.ClickCount == 2)
            ResetView();
        Focus();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _last = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_last is not { } last) return;
        var p = e.GetPosition(this);
        float dx = (float)(p.X - last.X), dy = (float)(p.Y - last.Y);
        _last = p;
        if (_panning)
        {
            var right = new Vector3(MathF.Cos(_yaw), 0, -MathF.Sin(_yaw));
            float k = _distance * 0.0015f;
            if (_streaming)
            {
                var ahead = -new Vector3(MathF.Sin(_yaw), 0, MathF.Cos(_yaw));
                _pan += (-right * dx + ahead * dy) * k;
            }
            else
                _pan += (-right * dx + Vector3.UnitY * dy) * k;
        }
        else
        {
            _yaw -= dx * 0.008f;
            _pitch = Math.Clamp(_pitch + dy * 0.008f, -1.55f, 1.55f);
        }
        RequestNextFrameRendering();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        float max = _streaming ? MathF.Max(4000f, _viewRadius * 0.8f) : _radius * 40f;
        _distance = Math.Clamp(_distance * (e.Delta.Y > 0 ? 1f / 1.1f : 1.1f), MathF.Min(_radius * 0.05f, 1f), max);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    protected override void OnSizeChanged(Avalonia.Controls.SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        RequestNextFrameRendering();
    }
}
