// See https://aka.ms/new-console-template for more information
using System.Runtime.InteropServices;
using WaySharp;
using WaySharp.Protocol;
using Buffer = WaySharp.Buffer;

AppDomain.CurrentDomain.FirstChanceException += (_, ex) =>
{
	Console.WriteLine($"First-chance exception: [{ex.Exception.GetType().Name}] {ex.Exception.Message}");
};

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
	Console.WriteLine($"Unhandled exception: [{e.ExceptionObject.GetType().Name}] {(e.ExceptionObject as Exception)?.Message}");
	if (e.ExceptionObject is Exception ex)
		Console.WriteLine(ex.StackTrace);
};


Compositor? compositor = null;
SharedMemory? shm = null;
Surface? surface = null;
IXdgSurface? xdgSurface = null;
IXdgToplevel? topLevel = null;
List<Output> outputs = [];
WaySharp.Protocol.IXdgWmBase? xdg = null;
bool running = true;

var display = Display.Connect();
var registry = display.GetRegistry();
registry.Global += (name, itf, version) =>
{
	//Console.WriteLine($"Got global '{itf}' ({name}, version {version})");
	switch (itf)
	{
		case var v when v.Equals(WaylandInterfaceDefinition.Compositor.Name):
			compositor = registry.Bind<Compositor>(name, 4);
			break;
		case var v when v.Equals(WaylandInterfaceDefinition.SharedMemory.Name):
			shm = registry.Bind<SharedMemory>(name, 1);
			break;
		case var v when v.Equals(WaylandInterfaceDefinition.Output.Name):
		{
			var output = registry.Bind<Output>(name, 4);
			//output.DescriptionChanged += (o,e)=>Console.WriteLine($"Description changed: {((Output)o).Description}");
			outputs.Add(output);
		} break;
		case var v when v.Equals(Registry.GetInterfaceName<IXdgWmBase>()):
			xdg = registry.Bind<IXdgWmBase>(name, 1);
			Console.WriteLine($"XDG handle: 0x{((IWaylandInterface)xdg).Handle.DangerousGetHandle()}");
			xdg.Ping += serial => xdg.Pong(serial);
			break;
	}
};
display.Roundtrip();

if (compositor is null || xdg is null || shm is null)
{
	Console.Error.WriteLine("Couldn't connect to compositor, or compositor doesn't support XDG Shell extensions.");
	return;
}

int width = 640;
int height = 480;

Console.WriteLine("Creating surface");
surface = compositor.CreateSurface() ?? throw new Exception("Surface is null");
Console.WriteLine("Creating XDG surface");
xdgSurface = xdg.GetXdgSurface(surface) ?? throw new Exception("XDG Surface is null");
Console.WriteLine($"0x{xdgSurface.Handle.DangerousGetHandle():x}");
xdgSurface.Configured += serial => {
	Console.WriteLine("XdgSurface configured");
	xdgSurface.AckConfigure(serial);
	var buffer = DrawFrame();
	surface.Attach(buffer, 0, 0);
	surface.Commit();
};
Console.WriteLine("Creating XDG toplevel");
// xdgSurface events
topLevel = xdgSurface.GetToplevel();
topLevel.Closed += () => {
	running = false;
};
topLevel.Configured += (width_, height_, states_) =>
{
	width = width_;
	height = height_;
};
Console.WriteLine("Setting title");
topLevel.SetTitle("Example Client");
surface.Commit();

Console.WriteLine("Running dispatch loop");

while (running && display.Dispatch() != 0) {}

Buffer? DrawFrame()
{
	int w = width, h = height;
	int stride = w * 4;
	int size = stride * h;
	using var shmFile = SharedMemory.Allocate(size);
	Console.WriteLine($"SHM File: file={shmFile.File.Handle}, memory={shmFile.Memory:x}h, size={shmFile.Size}");
	if (!shmFile.File.IsOpen)
		return null;
	Buffer buffer;
	using (var pool = shm.CreatePool(shmFile.File, size))
	{
		buffer = pool.CreateBuffer(0, w, h, stride, SharedMemoryFormat.XRGB8888);
	}

	// draw checkerboard
	for (int y = 0; y < h; ++y)
	{
		for (int x = 0; x < w; ++x)
		{
			if ((x + y / 8 * 8) % 16 < 8)
				Marshal.WriteInt32(shmFile.Memory, y * stride + x * 4, unchecked((int)0xFF666666));
			else
				Marshal.WriteInt32(shmFile.Memory, y * stride + x * 4, unchecked((int)0xFFEEEEEE));
		}
	}
	buffer.Released += delegate { buffer.Destroy(); };
	return buffer;
}
