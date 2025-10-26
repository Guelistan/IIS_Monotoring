# Bildbearbeitung – UML-Klassendiagramm (mit Connected Components & Schnittstellen)

Dieses Dokument zeigt eine modulare Architektur für eine Bildbearbeitungsbibliothek. Im Fokus stehen klare Schnittstellen, eine erweiterbare Pipeline, verbundene Komponenten (Connected Components) sowie die Trennung von I/O, Rechen-Backends und Erweiterungen.

Hinweis: Das Diagramm ist technologie-agnostisch und kann in C#, Java, TypeScript u. a. umgesetzt werden. Typparameter wie `Image<TPixel>` sind generisch gedacht.

## Klassendiagramm (Mermaid)

```mermaid
classDiagram
    direction LR

    %% --- Core-Bild- und Pixeltypen ---
    class IImageBuffer {
      +int Width
      +int Height
      +PixelFormat Format
      +Span~byte~ GetRow(int y)
    }

    class Image~TPixel~ {
      +int Width
      +int Height
      +TPixel[] Data
      +TPixel this[int x, int y]
      +Image~TPixel~ Clone()
    }
    IImageBuffer <|.. Image~TPixel~

    class IPixel {
      +byte[] ToBytes()
    }
    class Gray8
    class Rgb24
    class Rgba32
    IPixel <|.. Gray8
    IPixel <|.. Rgb24
    IPixel <|.. Rgba32
    Image~TPixel~ --> IPixel : TPixel implements

    class ROI {
      +Rect Bounds
    }
    class Mask {
      +bool[][] Data
    }
    Image~TPixel~ o-- ROI
    Image~TPixel~ o-- Mask

    class Rect
    class PointF

    %% --- I/O & Codecs ---
    class IImageSource {
      +Task~Image~TPixel~~ ReadAsync(string path)
      +Task~Image~TPixel~~ ReadAsync(Stream stream)
    }
    class IImageSink {
      +Task WriteAsync(Image~TPixel~ img, string path)
      +Task WriteAsync(Image~TPixel~ img, Stream stream)
    }
    class IImageCodec {
      +bool CanDecode(Stream s)
      +bool CanEncode(Image~TPixel~ img)
      +Image~TPixel~ Decode(Stream s)
      +void Encode(Image~TPixel~ img, Stream s)
    }

    class FileImageSource
    class FileImageSink
    IImageSource <|.. FileImageSource
    IImageSink <|.. FileImageSink

    class PngCodec
    class JpegCodec
    class BmpCodec
    IImageCodec <|.. PngCodec
    IImageCodec <|.. JpegCodec
    IImageCodec <|.. BmpCodec
    FileImageSource --> IImageCodec : uses
    FileImageSink --> IImageCodec : uses

    %% --- Pipeline & Stages ---
    class Context {
      +CancellationToken Token
      +IComputeBackend Compute
      +ICache Cache
      +ILogger Logger
    }

    class IPipelineStage {
      +string Name
      +Image~TPixel~ Process(Image~TPixel~ input, in Context ctx)
    }

    class Pipeline {
      +AddStage(IPipelineStage stage)
      +RemoveStage(IPipelineStage stage)
      +Image~TPixel~ Run(Image~TPixel~ input, in Context ctx)
    }
    Pipeline o-- IPipelineStage : stages
    Pipeline --> Context

    class IFilter {
      +Image~TPixel~ Apply(Image~TPixel~ input)
    }
    class FilterStage
    IPipelineStage <|.. FilterStage
    FilterStage --> IFilter

    class ConvolutionFilter {
      +Kernel Kernel
    }
    class GaussianBlur
    class Sharpen
    IFilter <|-- ConvolutionFilter
    IFilter <|-- GaussianBlur
    IFilter <|-- Sharpen

    class Threshold {
      +int Level
      +Image~Gray8~ Binarize(Image~Gray8~ input)
    }
    IFilter <|-- Threshold

    class Morphology {
      +Image~Gray8~ Erode(Image~Gray8~)
      +Image~Gray8~ Dilate(Image~Gray8~)
    }
    IFilter <|-- Morphology

    class ColorConversion {
      +Image~Gray8~ ToGray(Image~Rgb24~)
      +Image~Rgb24~ ToRgb(Image~Gray8~)
    }
    IPipelineStage <|.. ColorConversion

    %% --- Connected Components (verbundene Komponenten) ---
    class IConnectedComponentLabeler {
      +LabelMap Label(Image~Gray8~ binary)
      +ComponentStats[] Stats(LabelMap map)
    }
    class TwoPassLabeler
    class UnionFindLabeler
    IConnectedComponentLabeler <|.. TwoPassLabeler
    IConnectedComponentLabeler <|.. UnionFindLabeler

    class LabelMap {
      +int Width
      +int Height
      +int[,] Labels
      +int LabelCount
    }

    class ComponentStats {
      +int Label
      +int Area
      +Rect BBox
      +PointF Centroid
      +double Perimeter
      +double Compactness
    }

    class ComponentGraph {
      +Graph Build(LabelMap map)
    }

    TwoPassLabeler --> LabelMap : produces
    UnionFindLabeler --> LabelMap : produces
    LabelMap --> ComponentStats : summarizes
    ComponentGraph --> LabelMap : analyzes

    %% --- Compute-Backends ---
    class IComputeBackend {
      +IImageBuffer Map(Image~TPixel~ img)
      +void ParallelFor(int from, int to, Action~int~ body)
      +Image~TPixel~ Convolve(Image~TPixel~, Kernel)
    }
    class CpuBackend
    class GpuBackend
    IComputeBackend <|.. CpuBackend
    IComputeBackend <|.. GpuBackend
    IPipelineStage --> IComputeBackend : executes on

    %% --- Cache & Logging ---
    class ICache {
      +bool TryGet(string key, out object value)
      +void Set(string key, object value, TimeSpan ttl)
    }
    class InMemoryCache
    ICache <|.. InMemoryCache
    Pipeline --> ICache : memoize

    class ILogger {
      +void LogDebug(string msg)
      +void LogError(string msg, Exception ex)
    }
    Pipeline --> ILogger

    %% --- Plugins/Erweiterbarkeit ---
    class IPlugin {
      +void Register(PluginContext ctx)
    }
    class PluginContext {
      +void AddStage(Func~IPipelineStage~ factory)
      +void AddCodec(IImageCodec codec)
    }
    class PluginManager {
      +void LoadFrom(string path)
      +IEnumerable~IPipelineStage~ GetStages()
    }
    class ConnectedComponentsPlugin

    IPlugin <|.. ConnectedComponentsPlugin
    PluginManager o-- IPlugin
    PluginManager --> Pipeline : extend
    PluginContext --> Pipeline
    PluginContext --> IImageCodec

    %% --- Typalias / Hinweise ---
    class BinaryImage~stereotype~
    note for BinaryImage~stereotype~ "Alias: Image<Gray8> nach Threshold"
    BinaryImage~stereotype~ ..> Image~Gray8~ : is alias of
```

## Anmerkungen zur Architektur
- Trennung der Verantwortlichkeiten: Core-Bildtypen, I/O, Verarbeitung (Pipeline), Analyse (Connected Components), Compute-Backends und Erweiterbarkeit (Plugins).
- `Pipeline` orchestriert eine Liste von `IPipelineStage` und ermöglicht flexible Verarbeitungsketten. Caching/Logging sind zentral eingebunden über `Context`.
- Connected Components werden über eine `IConnectedComponentLabeler`-Schnittstelle abstrahiert, mit austauschbaren Implementierungen (z. B. Two-Pass, Union-Find). Statistiken und ein Komponenten-Graph sind getrennte Concern.
- `IComputeBackend` ermöglicht CPU- oder GPU-Implementierungen ohne Änderung der Algorithmen-Schnittstellen.
- I/O ist über `IImageSource`/`IImageSink` und `IImageCodec` flexibel erweiterbar; zusätzliche Formate können als Plugins geliefert werden.

## Mögliche Sequenz (Beispiel)
1. `FileImageSource.ReadAsync(path)` liest ein Bild und nutzt dafür einen passenden `IImageCodec`.
2. `Pipeline.Run(image, ctx)` führt nacheinander Stages aus: `ColorConversion -> Threshold -> Morphology`.
3. Das Ergebnis (binär) wird an `IConnectedComponentLabeler.Label(...)` übergeben; `Stats` werden berechnet und optional `ComponentGraph` erstellt.

## Hinweise zur Nutzung
- Du kannst dieses Diagramm in VS Code in der Markdown-Vorschau anzeigen. Mermaid wird von vielen Renderern unterstützt.
- Die Klassen- und Methodensignaturen sind als API-Vorschlag zu verstehen und können an deine Sprache/Runtime angepasst werden (z. B. Generics, Sync/Async, ValueTypes).
