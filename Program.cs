//#define SHELL
// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using SkiaSharp;
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
ISubcompositor? subcompositor = null;
SharedMemory? shm = null;
Surface? surface = null;
Surface? subsurface_surface = null;
ISubsurface? subsurface = null;
IZwlrLayerSurface? layerSurface = null;
IXdgSurface? xdgSurface = null;
IXdgToplevel? topLevel = null;
IZwlrLayerShell? shell = null;
ISeat? seat = null;
IPointer? pointer = null;
List<Output> outputs = [];
SKFont font = new (SKTypeface.Default, size: 20) { Edging = SKFontEdging.Antialias, Embolden = true };
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
		case var v when v.Equals(WaylandInterfaceDefinition.Subcompositor.Name):
			subcompositor = registry.Bind<ISubcompositor>(name, 1);
			break;
		case var v when v.Equals(WaylandInterfaceDefinition.Seat.Name):
			seat = registry.Bind<ISeat>(name, 1);
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
		case var v when v.Equals(Registry.GetInterfaceName<IZwlrLayerShell>()):
			shell = registry.Bind<IZwlrLayerShell>(name, 5);
			break;
	}
};
display.Roundtrip();

if (compositor is null || xdg is null || shm is null || shell is null || seat is null)
{
	Console.Error.WriteLine("Couldn't connect to compositor, or compositor doesn't support XDG Shell extensions.");
	return;
}



int width = 640;
int height = 480;
bool dirty = true;
FixedPoint curX = default, curY = default;

Console.WriteLine("Creating surface");
surface = compositor.CreateSurface() ?? throw new Exception("Surface is null");
pointer = seat.GetPointer() ?? throw new Exception("Couldn't get reference to a pointer device.");

Console.WriteLine("Creating XDG surface");
#if SHELL
layerSurface = shell.GetLayerSurface(surface, outputs[0], ShellLayer.Top, "test");
layerSurface.Configured += (serial_, width_, height_) =>
{
	width = (int)width_;
	height = (int)height_;
	layerSurface.AckConfigure(serial_);
	dirty = true;
	RenderInternal();
};
layerSurface.SetSize(0, 40);
layerSurface.SetAnchor(ShellAnchor.Top | ShellAnchor.Left | ShellAnchor.Right);
layerSurface.SetExclusiveEdge(ShellAnchor.Top);
surface.Commit();
#else
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

pointer.Motion += (_, x, y) =>
{
	Console.WriteLine($"({x}, {y})");
	curX = x; curY = y;
	var buffer = DrawFrame();
	surface.Attach(buffer, 0, 0);
	surface.DamageBuffer(4, 4, width, 100);
	surface.Commit();
};

subsurface_surface = compositor.CreateSurface();
subsurface = subcompositor.GetSubsurface(subsurface_surface, surface);
subsurface.PlaceAbove(surface);
var region = compositor.CreateRegion();
region.Add(0, 0, int.MaxValue, int.MaxValue);
subsurface_surface.SetOpaqueRegion(region);
int buttonWidth = 140, buttonHeight = 36;
int buttonSizeBytes = buttonWidth * buttonHeight * 4;
//Buffer buffer;
//using (var shmFile = SharedMemory.Allocate(buttonSizeBytes))
//{
//	using (var pool = shm.CreatePool(shmFile.File, buttonSizeBytes))
//		buffer = pool.CreateBuffer(0, buttonWidth, buttonHeight, 4, SharedMemoryFormat.ARGB8888);
//	var buttonImageInfo = new SKImageInfo(buttonWidth, buttonHeight);
//	using (var buttonImage = SKSurface.Create(buttonImageInfo, shmFile.Memory, 4))
//	{
//		SKCanvas buttonCanvas = buttonImage.Canvas;
//	}
//	//subsurface_surface.Attach(
//}

#endif
surface.Commit();

Console.WriteLine("Running dispatch loop");

while (running && display.Dispatch() != 0) {}

Buffer? DrawFrame()
{
	var stopwatch = Stopwatch.StartNew();
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

	var imageInfo = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
	using (var imageSurface = SKSurface.Create(imageInfo, shmFile.Memory, stride))
	{
		SKCanvas canvas = imageSurface.Canvas;
		canvas.Clear(SKColors.Transparent);
		const int GRIDSIZE = 20;
		using (var dark = new SKPaint { Color = new SKColor(unchecked(0xFF999999)), IsAntialias = false, IsStroke = false })
		using (var light = new SKPaint { Color = new SKColor(unchecked(0xFFEEEEEE)), IsAntialias = false, IsStroke = false })
		{
			for (int y = 0, row = 0; y < h; y += GRIDSIZE, ++row)
			{
				for (int x = 0, n = row & 1; x < w; x += GRIDSIZE, ++n)
				{
					canvas.DrawRect(SKRect.Create(x, y, GRIDSIZE, GRIDSIZE), ((n & 1) == 0) ? light : dark);
				}
			}
		}

		using (var textColour = new SKPaint { Color = new SKColor(unchecked((uint)0xFF000000)), IsAntialias = true })
		{
			canvas.DrawText($"({curX}, {curY})", new SKPoint(4, 54), SKTextAlign.Left, font, textColour);
			canvas.DrawText($"{w}x{h}px ({stopwatch.Elapsed.TotalMilliseconds:F2}ms)", new SKPoint(4, 24), SKTextAlign.Left, font, textColour);
		}
	}

	// draw checkerboard
	//for (int y = 0; y < h; ++y)
	//{
	//	for (int x = 0; x < w; ++x)
	//	{
	//		if ((x + y / 8 * 8) % 16 < 8)
	//			Marshal.WriteInt32(shmFile.Memory, y * stride + x * 4, unchecked((int)0xFF666666));
	//		else
	//			Marshal.WriteInt32(shmFile.Memory, y * stride + x * 4, unchecked((int)0xFFEEEEEE));
	//	}
	//}
	buffer.Released += delegate { buffer.Destroy(); };
	return buffer;
}

#if SHELL
void RenderInternal()
{
	if (width == 0 || height == 0) return;
	if (!dirty) return;

	// get graphics buffer
	// use graphics buffer
	// render to graphics buffer
	// set scale
	// attach
	// damage
	// create frame callback listener on surface
	// commit
}
#endif

class GCEventListener : EventListener
{
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name.Equals("Microsoft-Windows-DotNETRuntime"))
        {
            EnableEvents(eventSource, EventLevel.Informational, (EventKeywords)0x1); // GC keyword
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        Console.WriteLine($"GC Event: {eventData.EventName}");
        foreach (var payload in eventData.Payload ?? [])
        {
            Console.WriteLine($"  {payload}");
        }
    }
}
