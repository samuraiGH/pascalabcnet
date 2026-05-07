/// Модуль визуализации на базе InteractiveDataDisplay (WPF).
/// ВАЖНО:
/// • Требует Windows и WPF.
/// • Использует GAC-сборку InteractiveDataDisplay.WPF.
/// • Не поддерживается на Linux/macOS и в .NET Core без дополнительной настройки.
/// Рекомендуется использовать только в desktop-сценариях.
unit PlotML;

{$reference %GAC%\InteractiveDataDisplay.WPF.dll}
{$reference 'PresentationFramework.dll'}
{$reference 'WindowsBase.dll'}
{$reference 'PresentationCore.dll'}

interface

uses System,
     System.Windows,
     System.Windows.Controls,
     System.Windows.Media,
     System.Windows.Shapes,
     System.Threading,
     System.Windows.Threading,
     System.Text,
     InteractiveDataDisplay.WPF,
     LinearAlgebraML;
     
type
  ApplicationWPF = System.Windows.Application;
  WindowWPF = System.Windows.Window;
  GridWPF = System.Windows.Controls.Grid;
  ChartWPF = InteractiveDataDisplay.WPF.Chart;
  LineGraphWPF = InteractiveDataDisplay.WPF.LineGraph;
  MarkerGraphWPF = InteractiveDataDisplay.WPF.CircleMarkerGraph;
  PlotWPF = InteractiveDataDisplay.WPF.Plot;
  BrushWPF = System.Windows.Media.SolidColorBrush;
  Colors = System.Windows.Media.Colors;
  ColorWPF = System.Windows.Media.Color;
  Matrix = LinearAlgebraML.Matrix; // кто-то еще определяет Matrix и Vector поэтому переопределяю здесь
  Vector = LinearAlgebraML.Vector;
  
const DefaultColor = default(ColorWPF);
  
type
  MarkerType = (Circle, Box, Triangle, Diamond, Cross);

  Palette = class
  public
    Colors: array of Color;
    constructor Create(params c: array of Color);
  end;
  
  PaletteWPF = Palette;
  
  Palettes = static class
  private
    static paletteDict: Dictionary<string, Palette>;
    
    static constructor;
    static procedure InitPalettes;
  
  public
    static function &Default: Palette;
    static function Pastel: Palette;
    static function Dark: Palette;
    static function Bright: Palette;
    static function Muted: Palette;
  
    static procedure Register(name: string; p: Palette);
    
    static function Get(name: string): Palette;
  end;
  
  Figure = class;

  Cell = class
  private
    parentGrid: GridWPF;
    row,col: integer;
    chart: ChartWPF;

    palette: Palette;
    paletteIndex: integer := 0;

    function NextColor: Color;
    procedure EnsureChart;
    
    procedure SetTitle(s: string);
  public
    constructor Create(g: GridWPF; r,c: integer);

    procedure LineGraph(x, y: array of real; color: ColorWPF := DefaultColor; 
      thickness: real := 2; legend: string := nil);
    procedure Points(x, y: array of real; color: ColorWPF := DefaultColor; 
      size: real := 6; marker: MarkerType := MarkerType.Circle; legend: string := nil);
    procedure Points(x, y: array of real; labels: array of integer;
      size: real := 6; marker: MarkerType := MarkerType.Circle);  
    procedure Hist(x: array of real; bins: integer := 0; 
      color: ColorWPF := DefaultColor; alpha: real := 0.7; legend: string := nil);
    procedure Heatmap(m: array[,] of real);
      
// --- Vector overloads
    procedure LineGraph(x, y: Vector; color: ColorWPF := DefaultColor;
      thickness: real := 2; legend: string := nil);
    procedure Points(x, y: Vector; color: ColorWPF := DefaultColor;
      size: real := 6; marker: MarkerType := MarkerType.Circle; legend: string := nil);
    procedure Points(x, y: Vector; labels: array of integer;
      size: real := 6; marker: MarkerType := MarkerType.Circle);
    procedure Hist(x: Vector; bins: integer := 0;
      color: ColorWPF := DefaultColor; alpha: real := 0.7; legend: string := nil);
      
    procedure Surface(x1, x2: array of real; nx, ny: integer; f: Matrix -> array of integer; pal: PlotML.Palette := nil);      
      
    procedure Heatmap(m: Matrix);
    procedure HeatCell(value, minValue, maxValue: real; text: string := nil);
    
    procedure Text(s: string; x: real := 0.5; y: real := 0.5);
    
    procedure SetPalette(p: PaletteWPF);
    procedure XLabel(s: string);
    procedure YLabel(s: string);
    
    procedure Limits(xmin, xmax, ymin, ymax: real);
    procedure XLim(xmin,xmax: real);
    procedure YLim(ymin,ymax: real);
    
    procedure Clear;
    
    property Title: string write SetTitle;
  end;

  Figure = class
  private
    grid: GridWPF;
    cells: array[,] of Cell;
  public
    constructor Create(rows,cols: integer);
    property Item[r,c: integer]: Cell read cells[r,c]; default;
  end;

  Plot = static class
  private
    static procedure RunUI(a: procedure);

    static function CreateLineSeries(x,y: array of real; c: Color): LineGraphWPF;
    static function CreatePointSeries(x, y: array of real; 
      color: ColorWPF; size: real; marker: MarkerType): MarkerGraphWPF;
    
    static procedure DrawLine(chart: ChartWPF; x, y: array of real;
      color: ColorWPF; thickness: real; legend: string);
      
    static procedure DrawText(chart: ChartWPF; s: string; x, y: real);  
  
    static procedure DrawPoints(chart: ChartWPF; x, y: array of real;
      color: ColorWPF; size: real; marker: MarkerType; legend: string);
      
    static procedure DrawHeatmap(chart: ChartWPF; m: array[,] of real; names: array of string := nil);  
    
    static procedure DrawHist(chart: ChartWPF; x: array of real;
      bins: integer; color: ColorWPF; alpha: real; legend: string);
      
    static procedure DrawHistMany(chart: ChartWPF; arrays: array of array of real;
      bins: integer; colors: array of ColorWPF; alpha: real; legends: array of string);
      
    static procedure DrawSurface(chart: ChartWPF; labels: array of integer; 
      nx, ny: integer; xmin, xmax, ymin, ymax: real; pal: Palette);
      
    static function MakeGrid(xmin, xmax, ymin, ymax: real; nx, ny: integer): Matrix;
      
    static procedure SetTitle(s: string);
  public
    static procedure AddSeries(chart: ChartWPF; series: UIElement);

    static procedure LineGraph(x, y: array of real;
      color: ColorWPF := DefaultColor; thickness: real := 2; legend: string := nil);
      
    static procedure Points(x, y: array of real; 
      color: ColorWPF := DefaultColor; size: real := 6; marker: MarkerType := MarkerType.Circle; legend: string := nil);
      
    static procedure Points(x, y: array of real;
      labels: array of integer; color: ColorWPF := DefaultColor; size: real := 6; marker: MarkerType := MarkerType.Circle);
      
    static procedure Hist(x: array of real; bins: integer := 0;
      color: ColorWPF := DefaultColor; alpha: real := 0.7; legend: string := nil);

    static procedure HistMany(arrays: array of array of real; bins: integer := 0;
      colors: array of ColorWPF := nil; alpha: real := 0.7; legend: array of string := nil);
      
    static procedure PairPlot(X: array[,] of real; labels: array of integer; names: array of string);
    
    static procedure Heatmap(m: array[,] of real);
    static procedure Heatmap(m: array[,] of real; names: array of string);
    
    static procedure Surface(labels: array of integer; nx, ny: integer; xmin, xmax, ymin, ymax: real; pal: PlotML.Palette := nil);

    static procedure Surface(x1, x2: array of real; nx, ny: integer; f: Matrix -> array of integer; pal: PlotML.Palette := nil);

// с матрицами - векторами   
   
    static procedure LineGraph(x, y: Vector;
      color: ColorWPF := DefaultColor; thickness: real := 2; legend: string := nil) 
      := LineGraph(x.Data, y.Data, color, thickness, legend);
    
    static procedure Points(x, y: Vector; color: ColorWPF := DefaultColor; size: real := 6;
      marker: MarkerType := MarkerType.Circle; legend: string := nil)
      := Points(x.Data, y.Data, color, size, marker, legend);
    
    static procedure Points(x, y: Vector;
      labels: array of integer; color: ColorWPF := DefaultColor; size: real := 6;
      marker: MarkerType := MarkerType.Circle) 
      := Points(x.Data, y.Data, labels, color, size, marker);
    
    static procedure Hist(x: Vector; bins: integer := 0;
      color: ColorWPF := DefaultColor; alpha: real := 0.7; legend: string := nil)
      := Hist(x.Data, bins, color, alpha, legend);
      
    static procedure PairPlot(X: Matrix; labels: array of integer; names: array of string)
      := PairPlot(X.Data, labels, names);
    
    static procedure Heatmap(m: Matrix) := Heatmap(m.Data);
    static procedure Heatmap(m: Matrix; names: array of string) := Heatmap(m.Data, names);
    
    
    static function Grid(rows,cols: integer): Figure;

    static procedure SetPalette(p: PaletteWPF);
    
    static procedure EnsureAxes(chart: ChartWPF);
   
    static procedure Limits(xmin,xmax,ymin,ymax: real);
    static procedure XLim(xmin,xmax: real);
    static procedure YLim(ymin,ymax: real);
    static procedure XLabel(s: string);
    static procedure YLabel(s: string);
    static procedure SetLabels(title: string := ''; xlabel: string := ''; ylabel: string := '');
    
    static procedure Clear;
    
    static procedure Save(filename: string);

    static function DebugVisualTree: string;
    
    static property Title: string write SetTitle;
  end;

  HistogramPlot = class(PlotWPF)
  private
    fBins: List<Polygon>;
    fColor: ColorWPF;
    fAlpha: real;
    fBinsCount: integer;
    fDescription: string;
    fMaxCount: integer;
    MinValue: real := real.NaN;
    MaxValue: real := real.NaN;
   
  public
    constructor Create;
  
    procedure SetData(x: array of real);
  
    property Color: ColorWPF read fColor write fColor;
    property Alpha: real read fAlpha write fAlpha;
    property BinsCount: integer read fBinsCount write fBinsCount;
    property Description: string read fDescription write fDescription;
    property MaxCount: integer read fMaxCount;
  end;

  HeatmapPlot = class(PlotWPF)
  private
    fCells: List<Polygon> := new List<Polygon>;
    fMinValue: real;
    fMaxValue: real;

    function LerpColor(c1, c2: ColorWPF; t: real): ColorWPF;
    function Clamp01(x: real): real;
    function ColorForValue(v: real): ColorWPF;
  public
    constructor Create;

    procedure SetData(m: array[,] of real);

    property MinValue: real read fMinValue;
    property MaxValue: real read fMaxValue;
  end;
  
  SurfacePlot = class(PlotWPF)
  private
    fRects: List<Polygon> := new List<Polygon>;
  public
    procedure SetData(labels: array of integer;
      nx, ny: integer; xmin, xmax, ymin, ymax: real; pal: Palette);
  end;

implementation

var
  uiThread: Thread;
  uiDispatcher: Dispatcher;

  app: ApplicationWPF;
  win: WindowWPF;
  rootChart: ChartWPF;

  paletteDict: Dictionary<string,Palette>;
  currentPalette: Palette;
  rootPaletteIndex: integer := 0;
  
static function Palettes.&Default: Palette := paletteDict['default'];
static function Palettes.Pastel: Palette := paletteDict['pastel'];
static function Palettes.Dark: Palette := paletteDict['dark'];
static function Palettes.Bright: Palette := paletteDict['bright'];
static function Palettes.Muted: Palette := paletteDict['muted'];

static constructor Palettes.Create;
begin
  InitPalettes
end;

static procedure Palettes.Register(name: string; p: Palette);
begin
  paletteDict[name] := p;
end;

static function Palettes.Get(name: string): Palette;
begin
  Result := paletteDict[name];
end;

static procedure Palettes.InitPalettes;
begin
  if paletteDict <> nil then exit;
  
  paletteDict := new Dictionary<string,Palette>;

  paletteDict['default'] := new Palette(
    Color.FromRgb($1f,$77,$b4),
    Color.FromRgb($ff,$7f,$0e),
    Color.FromRgb($2c,$a0,$2c),
    Color.FromRgb($d6,$27,$28),
    Color.FromRgb($94,$67,$bd),
    Color.FromRgb($8c,$56,$4b),
    Color.FromRgb($e3,$77,$c2),
    Color.FromRgb($7f,$7f,$7f),
    Color.FromRgb($bc,$bd,$22),
    Color.FromRgb($17,$be,$cf)
  );

  paletteDict['pastel'] := new Palette(
    Color.FromRgb(141,211,199),
    Color.FromRgb(255,255,179),
    Color.FromRgb(190,186,218),
    Color.FromRgb(251,128,114),
    Color.FromRgb(128,177,211),
    Color.FromRgb(253,180,98),
    Color.FromRgb(179,222,105),
    Color.FromRgb(252,205,229)
  );

  paletteDict['dark'] := new Palette(
    Color.FromRgb(27,158,119),
    Color.FromRgb(217,95,2),
    Color.FromRgb(117,112,179),
    Color.FromRgb(231,41,138),
    Color.FromRgb(102,166,30),
    Color.FromRgb(230,171,2),
    Color.FromRgb(166,118,29),
    Color.FromRgb(102,102,102)
  );

  paletteDict['bright'] := new Palette(
    Color.FromRgb(0,114,178),
    Color.FromRgb(230,159,0),
    Color.FromRgb(0,158,115),
    Color.FromRgb(213,94,0),
    Color.FromRgb(204,121,167),
    Color.FromRgb(86,180,233)
  );
  
  paletteDict['muted'] := new Palette(
    Color.FromRgb(76,114,176),
    Color.FromRgb(221,132,82),
    Color.FromRgb(85,168,104),
    Color.FromRgb(196,78,82),
    Color.FromRgb(129,114,179),
    Color.FromRgb(147,120,96)
  );
  
  currentPalette := paletteDict['default'];
end;
  
function CreateMarker(t: MarkerType): InteractiveDataDisplay.WPF.ColorSizeMarker;
begin
  case t of
    MarkerType.Circle:   Result := new CircleMarker;
    MarkerType.Box:      Result := new BoxMarker;
    MarkerType.Triangle: Result := new TriangleMarker;
    MarkerType.Diamond:  Result := new DiamondMarker;
    MarkerType.Cross:    Result := new CrossMarker;
  end;
end;
  
function NextRootColor: ColorWPF;
begin
  var c := currentPalette.Colors[
    rootPaletteIndex mod currentPalette.Colors.Length
  ];

  rootPaletteIndex += 1;
  Result := c;
end;

function Clamp01(x: real): real;
begin
  if x < 0 then
    Result := 0
  else if x > 1 then
    Result := 1
  else
    Result := x;
end;

function LerpColor(c1, c2: ColorWPF; t: real): ColorWPF;
begin
  t := Clamp01(t);

  Result := Color.FromRgb(
    byte(Round(c1.R + (c2.R - c1.R) * t)),
    byte(Round(c1.G + (c2.G - c1.G) * t)),
    byte(Round(c1.B + (c2.B - c1.B) * t))
  );
end;

function HeatmapColor(v, minValue, maxValue: real): ColorWPF;
begin
  if real.IsNaN(v) or real.IsInfinity(v) then
    exit(Color.FromRgb(180, 180, 180));

  if minValue = maxValue then
    exit(Color.FromRgb(255, 255, 255));

  var blue := Color.FromRgb(49, 130, 189);
  var white := Color.FromRgb(255, 255, 255);
  var red := Color.FromRgb(222, 45, 38);

  if (minValue < 0) and (maxValue > 0) then
  begin
    if v < 0 then
      Result := LerpColor(white, blue, Abs(v / minValue))
    else
      Result := LerpColor(white, red, v / maxValue);
    exit;
  end;

  Result := LerpColor(blue, red, (v - minValue) / (maxValue - minValue));
end;

procedure DumpVisualNode(sb: StringBuilder; obj: DependencyObject; level: integer);
begin
  if obj = nil then
    exit;

  var fe := obj as FrameworkElement;
  var name := if (fe <> nil) and (fe.Name <> nil) and (fe.Name <> '') then fe.Name else '-';

  sb.Append(''.PadLeft(level * 2));
  sb.Append(obj.GetType.FullName);
  sb.Append('  Name=');
  sb.AppendLine(name);

  var cnt := VisualTreeHelper.GetChildrenCount(obj);
  for var i := 0 to cnt - 1 do
    DumpVisualNode(sb, VisualTreeHelper.GetChild(obj, i), level + 1);
end;

function MakeHistogram(data: array of real; bins: integer): (array of real, array of real);
begin
  var xmin := data.Min;
  var xmax := data.Max;
  
  var h := (xmax-xmin)/bins;
  
  var counts := new real[bins];
  
  foreach var v in data do
  begin
    var k := integer((v-xmin)/h);
    if k=bins then k := bins-1;
    counts[k] += 1;
  end;
  
  var xs := ArrGen(bins, i -> xmin + (i+0.5)*h);
  
  Result := (xs,counts);
end;



procedure InitUI;
begin
  uiThread := new Thread(() ->
  begin
    app := new ApplicationWPF;

    app.Dispatcher.UnhandledException += (o,e) ->
    begin
      Println(e.Exception.Message);
      if e.Exception.InnerException <> nil then
        Println(e.Exception.InnerException.Message);
      halt;
    end;

    rootChart := new ChartWPF;
    rootChart.Margin := new Thickness(2);

    win := new WindowWPF;
    win.Title := 'PlotML';
    win.Width := 800;
    win.Height := 600;
    win.Content := rootChart;

    win.Closed += (o,e) ->
      Dispatcher.CurrentDispatcher.BeginInvokeShutdown(
        DispatcherPriority.Normal);

    uiDispatcher := Dispatcher.CurrentDispatcher;

    Palettes.InitPalettes;

    win.Show;

    Dispatcher.Run;
  end);

  uiThread.SetApartmentState(ApartmentState.STA);
  uiThread.Start;

  while uiDispatcher = nil do
    Sleep(10);
end;

constructor Palette.Create(params c: array of Color);
begin
  Colors := c;
end;

constructor Cell.Create(g: GridWPF; r,c: integer);
begin
  parentGrid := g;
  row := r;
  col := c;
  palette := currentPalette;
end;

function Cell.NextColor: ColorWPF;
begin
  var c := palette.Colors[
    paletteIndex mod palette.Colors.Length
  ];

  paletteIndex += 1;

  Result := c;
end;

procedure Cell.SetPalette(p: PaletteWPF);
begin
  if p = nil then exit;

  palette := p;
  paletteIndex := 0;
end;

procedure Cell.SetTitle(s: string);
begin
  Plot.RunUI(() ->
  begin
    EnsureChart;
    chart.Title := s;
  end);
end;

procedure Cell.XLabel(s: string);
begin
  Plot.RunUI(() ->
  begin
    EnsureChart;
    chart.BottomTitle := s;
  end);
end;

procedure Cell.YLabel(s: string);
begin
  Plot.RunUI(() ->
  begin
    EnsureChart;
    chart.LeftTitle := s;
  end);
end;

procedure Cell.Limits(xmin,xmax,ymin,ymax: real);
begin
  Plot.RunUI(() ->
  begin
    EnsureChart;

    chart.PlotOriginX := xmin;
    chart.PlotOriginY := ymin;

    chart.PlotWidth := xmax - xmin;
    chart.PlotHeight := ymax - ymin;
  end);
end;

procedure Cell.XLim(xmin,xmax: real);
begin
  Plot.RunUI(() ->
  begin
    EnsureChart;

    chart.PlotOriginX := xmin;
    chart.PlotWidth := xmax - xmin;
  end);
end;

procedure Cell.YLim(ymin,ymax: real);
begin
  Plot.RunUI(() ->
  begin
    EnsureChart;

    chart.PlotOriginY := ymin;
    chart.PlotHeight := ymax - ymin;
  end);
end;

procedure Cell.Clear;
begin
  Plot.RunUI(() ->
  begin
    if chart = nil then exit;

    var container := chart.Content as GridWPF;
    if container <> nil then
      container.Children.Clear;

    paletteIndex := 0;
  end);
end;

procedure Cell.EnsureChart;
begin
  if chart <> nil then exit;

  chart := new ChartWPF;
  chart.Margin := new Thickness(2);
  chart.LegendVisibility := Visibility.Hidden;

  var container := new GridWPF;
  chart.Content := container;

  GridWPF.SetRow(chart,row);
  GridWPF.SetColumn(chart,col);

  parentGrid.Children.Add(chart);
end;

procedure Cell.LineGraph(x, y: array of real; color: ColorWPF; 
  thickness: real; legend: string);
begin
  Plot.RunUI(() ->
  begin
    EnsureChart;

    var clr := if color<>DefaultColor then color else NextColor;

    Plot.DrawLine(chart, x, y, clr, thickness, legend);
  end);
end;

procedure Cell.Points(x, y: array of real; color: ColorWPF;
  size: real; marker: MarkerType; legend: string);
begin
  Plot.RunUI(() ->
  begin
    EnsureChart;

    var clr := if color<>DefaultColor then color else NextColor;

    Plot.DrawPoints(chart, x, y, clr, size, marker, legend);
  end);
end;

procedure Cell.Points(x, y: array of real; labels: array of integer;
  size: real; marker: MarkerType);
begin
  if (x = nil) or (y = nil) or (labels = nil) then
    raise new System.ArgumentNullException;

  if (x.Length <> y.Length) or (x.Length <> labels.Length) then
    raise new System.ArgumentException('Points: array sizes mismatch');

  var classes := labels.Distinct.ToArray;
  &Array.Sort(classes);

  var pal := palette;

  foreach var c in classes do
  begin
    var ind := labels.Indices(v -> v = c).ToArray;

    var xs := ind.ConvertAll(i -> x[i]);
    var ys := ind.ConvertAll(i -> y[i]);

    var clr := pal.Colors[c mod pal.Colors.Length];

    self.Points(xs, ys, clr, size, marker, nil);
  end;
end;

procedure Cell.Heatmap(m: array[,] of real);
begin
  Plot.RunUI(() ->
  begin
    EnsureChart;
    Plot.DrawHeatmap(chart, m);
  end);
end;

procedure Cell.HeatCell(value, minValue, maxValue: real; text: string);
begin
  Plot.RunUI(() ->
  begin
    EnsureChart;

    var container := chart.Content as GridWPF;
    if container = nil then
    begin
      container := new GridWPF;
      chart.Content := container;
    end;

    container.Children.Clear;
    container.Background := new SolidColorBrush(HeatmapColor(value, minValue, maxValue));

    if text <> nil then
    begin
      var tb := new System.Windows.Controls.TextBlock;
      tb.Text := text;
      tb.FontSize := 14;
      tb.FontWeight := System.Windows.FontWeights.Bold;
      tb.HorizontalAlignment := System.Windows.HorizontalAlignment.Center;
      tb.VerticalAlignment := System.Windows.VerticalAlignment.Center;
      container.Children.Add(tb);
    end;
  end);
end;

procedure Cell.Hist(x: array of real; bins: integer; color: ColorWPF; alpha: real; legend: string);
begin
  Plot.RunUI(() ->
  begin
    EnsureChart;

    var clr := if color<>DefaultColor then color else NextColor;

    Plot.DrawHist(chart, x, bins, clr, alpha, legend);
  end);
end;

procedure Cell.LineGraph(x, y: Vector; color: ColorWPF; thickness: real; legend: string);
begin
  LineGraph(x.Data, y.Data, color, thickness, legend);
end;

procedure Cell.Points(x, y: Vector; color: ColorWPF; size: real; marker: MarkerType; legend: string);
begin
  Points(x.Data, y.Data, color, size, marker, legend);
end;

procedure Cell.Points(x, y: Vector; labels: array of integer; size: real; marker: MarkerType);
begin
  Points(x.Data, y.Data, labels, size, marker);
end;

procedure Cell.Heatmap(m: Matrix);
begin
  Heatmap(m.Data);
end;

procedure Cell.Hist(x: Vector; bins: integer;
  color: ColorWPF; alpha: real; legend: string);
begin
  Hist(x.Data, bins, color, alpha, legend);
end;

procedure Cell.Text(s: string; x: real; y: real);
begin
  Plot.RunUI(() ->
  begin
    EnsureChart;

    Plot.DrawText(chart, s, x, y);
  end);
end;

constructor Figure.Create(rows,cols: integer);
begin
  grid := new GridWPF;

  for var i:=0 to rows-1 do
    grid.RowDefinitions.Add(new RowDefinition);

  for var j:=0 to cols-1 do
    grid.ColumnDefinitions.Add(new ColumnDefinition);

  cells := new Cell[rows,cols];

  for var i:=0 to rows-1 do
    for var j:=0 to cols-1 do
      cells[i,j] := new Cell(grid,i,j);
end;

static procedure Plot.RunUI(a: procedure);
begin
  if Dispatcher.CurrentDispatcher = uiDispatcher then
    a()
  else
    uiDispatcher.Invoke(a);
end;

static procedure Plot.Limits(xmin,xmax,ymin,ymax: real);
begin
  RunUI(() ->
  begin
    rootChart.PlotOriginX := xmin;
    rootChart.PlotOriginY := ymin;

    rootChart.PlotWidth := xmax - xmin;
    rootChart.PlotHeight := ymax - ymin;
  end);
end;

static procedure Plot.XLim(xmin,xmax: real);
begin
  RunUI(() ->
  begin
    rootChart.PlotOriginX := xmin;
    rootChart.PlotWidth := xmax - xmin;
  end);
end;

static procedure Plot.YLim(ymin,ymax: real);
begin
  RunUI(() ->
  begin
    rootChart.PlotOriginY := ymin;
    rootChart.PlotHeight := ymax - ymin;
  end);
end;

var gridMode := false;

static procedure Plot.SetTitle(s: string);
begin
  RunUI(() ->
  begin
    if gridMode then
      win.Title := s
    else
      rootChart.Title := s;
  end);
end;

static procedure Plot.XLabel(s: string);
begin
  RunUI(() ->
  begin
    rootChart.BottomTitle := s;
  end);
end;

static procedure Plot.YLabel(s: string);
begin
  RunUI(() ->
  begin
    rootChart.LeftTitle := s;
  end);
end;

static procedure Plot.SetLabels(title: string; xlabel: string; ylabel: string);
begin
  RunUI(() ->
  begin
    if title <> '' then
      rootChart.Title := title;

    if xlabel <> '' then
      rootChart.BottomTitle := xlabel;

    if ylabel <> '' then
      rootChart.LeftTitle := ylabel;
  end);
end;

static procedure Plot.Clear;
begin
  RunUI(() ->
  begin
    var container := rootChart.Content as GridWPF;
    if container <> nil then
      container.Children.Clear;
    rootPaletteIndex := 0;
  end);
end;

static procedure Plot.Save(filename: string);
begin
  RunUI(() ->
  begin
    var rtb := new System.Windows.Media.Imaging.RenderTargetBitmap(
      integer(win.Width),
      integer(win.Height),
      96, 96,
      PixelFormats.Pbgra32
    );

    rtb.Render(win);

    var encoder := new System.Windows.Media.Imaging.PngBitmapEncoder;
    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

    var fs := new System.IO.FileStream(filename, System.IO.FileMode.Create);
    encoder.Save(fs);
    fs.Close;
  end);
end;

static function Plot.DebugVisualTree: string;
begin
  var sb := new StringBuilder;

  RunUI(() ->
  begin
    if win = nil then
    begin
      sb.AppendLine('win = nil');
      exit;
    end;

    sb.AppendLine('Window content tree:');
    DumpVisualNode(sb, win.Content as DependencyObject, 0);
  end);

  Result := sb.ToString;
end;

static function Plot.CreateLineSeries(x,y: array of real; c: Color): LineGraphWPF;
begin
  var g := new LineGraphWPF;

  g.Stroke := new BrushWPF(c);
  g.StrokeThickness := 2;

  g.Plot(x,y);

  Result := g;
end;

static function Plot.CreatePointSeries(x, y: array of real; 
  color: ColorWPF; size: real; marker: MarkerType): MarkerGraphWPF;
begin
  var g := new MarkerGraphWPF;

  {var alpha := 0.5;
  var a := round(alpha * 255);
  var c := ColorWPF.FromArgb(a, color.R, color.G, color.B);
  
  g.Color := c;}
  
  g.Color := color;
  g.Size := size;
  g.MarkerType := CreateMarker(marker);
  g.StrokeThickness := 0;
  
  g.PlotColor(x,y,color);

  Result := g;
end;

static procedure Plot.DrawLine(chart: ChartWPF; x, y: array of real;
  color: ColorWPF; thickness: real; legend: string);
begin
  var g := CreateLineSeries(x, y, color);

  g.StrokeThickness := thickness;

  if legend <> nil then
  begin  
    g.Description := legend;
    chart.LegendVisibility := Visibility.Visible;
  end;  

  AddSeries(chart, g);
end;

static procedure Plot.DrawText(chart: ChartWPF; s: string; x, y: real);
begin
  var tb := new System.Windows.Controls.TextBlock;
  tb.Text := s;
  tb.FontSize := 14;
  tb.FontWeight := System.Windows.FontWeights.Bold;

  tb.HorizontalAlignment := System.Windows.HorizontalAlignment.Center;
  tb.VerticalAlignment := System.Windows.VerticalAlignment.Center;

  var grid := chart.Content as GridWPF;
  if grid = nil then
  begin
    grid := new GridWPF;
    chart.Content := grid;
  end;

  if grid <> nil then
  begin
    tb.HorizontalAlignment := System.Windows.HorizontalAlignment.Center;
    tb.VerticalAlignment := System.Windows.VerticalAlignment.Center;
    grid.Children.Add(tb);
  end;
end;

static procedure Plot.DrawPoints(chart: ChartWPF; x, y: array of real;
  color: ColorWPF; size: real; marker: MarkerType; legend: string);
begin
  var g := CreatePointSeries(x, y, color, size, marker);

  if legend <> nil then
  begin  
    g.Description := legend;
    chart.LegendVisibility := Visibility.Visible;
  end;

  AddSeries(chart, g);
end;

static procedure Plot.DrawHeatmap(chart: ChartWPF; m: array[,] of real; names: array of string);
begin
  var rows := m.GetLength(0);
  var cols := m.GetLength(1);

  var container := new GridWPF;
  chart.Content := container;

  var topOffset := if names <> nil then 1 else 0;
  var leftOffset := if names <> nil then 1 else 0;

  for var i := 0 to rows + topOffset - 1 do
    container.RowDefinitions.Add(new RowDefinition);

  for var j := 0 to cols + leftOffset - 1 do
    container.ColumnDefinitions.Add(new ColumnDefinition);

  if names <> nil then
  begin
    for var j := 0 to cols - 1 do
    begin
      var tb := new System.Windows.Controls.TextBlock;
      tb.Text := names[j];
      tb.FontSize := 13;
      tb.FontWeight := System.Windows.FontWeights.Normal;
      tb.TextAlignment := TextAlignment.Center;
      tb.TextWrapping := TextWrapping.Wrap;
      tb.Margin := new Thickness(6);
      tb.HorizontalAlignment := HorizontalAlignment.Center;
      tb.VerticalAlignment := VerticalAlignment.Center;
      GridWPF.SetRow(tb, 0);
      GridWPF.SetColumn(tb, j + 1);
      container.Children.Add(tb);
    end;

    for var i := 0 to rows - 1 do
    begin
      var tb := new System.Windows.Controls.TextBlock;
      tb.Text := names[i];
      tb.FontSize := 13;
      tb.FontWeight := System.Windows.FontWeights.Normal;
      tb.TextAlignment := TextAlignment.Center;
      tb.TextWrapping := TextWrapping.Wrap;
      tb.Margin := new Thickness(6);
      tb.HorizontalAlignment := HorizontalAlignment.Center;
      tb.VerticalAlignment := VerticalAlignment.Center;
      GridWPF.SetRow(tb, i + 1);
      GridWPF.SetColumn(tb, 0);
      container.Children.Add(tb);
    end;
  end;

  var valuesOnly := new HeatmapPlot;
  valuesOnly.SetData(m);
  var minValue := valuesOnly.MinValue;
  var maxValue := valuesOnly.MaxValue;

  for var i := 0 to rows - 1 do
    for var j := 0 to cols - 1 do
    begin
      var border := new Border;
      border.BorderBrush := Brushes.White;
      border.BorderThickness := new Thickness(0.25);
      border.Background := new SolidColorBrush(HeatmapColor(m[i, j], minValue, maxValue));

      var tb := new System.Windows.Controls.TextBlock;
      tb.Text := $'{m[i, j]:F2}';
      tb.FontSize := 15;
      tb.FontWeight := System.Windows.FontWeights.SemiBold;
      tb.TextAlignment := TextAlignment.Center;
      tb.HorizontalAlignment := HorizontalAlignment.Center;
      tb.VerticalAlignment := VerticalAlignment.Center;

      border.Child := tb;

      GridWPF.SetRow(border, i + topOffset);
      GridWPF.SetColumn(border, j + leftOffset);
      container.Children.Add(border);
    end;
end;

class procedure Plot.AddSeries(chart: ChartWPF; series: UIElement);
begin
  var container := chart.Content as GridWPF;

  if container = nil then
  begin
    container := new GridWPF;
    chart.Content := container;
  end;

  container.Children.Add(series);
end;

class procedure Plot.LineGraph(x, y: array of real;
  color: ColorWPF; thickness: real; legend: string);
begin
  RunUI(() ->
  begin
    var clr := if color<>DefaultColor then color else NextRootColor;

    DrawLine(rootChart, x, y, clr, thickness, legend);
  end);
end;

static procedure Plot.Points(x, y: array of real; 
  color: ColorWPF; size: real; marker: MarkerType; legend: string);
begin
  RunUI(() ->
  begin
    var clr := if color<>DefaultColor then color else NextRootColor;

    DrawPoints(rootChart, x, y, clr, size, marker, legend);
  end);
end;

static procedure Plot.Points(x, y: array of real; labels: array of integer;
  color: ColorWPF; size: real; marker: MarkerType);
begin
  if (x = nil) or (y = nil) or (labels = nil) then
    raise new System.ArgumentNullException;

  if (x.Length <> y.Length) or (x.Length <> labels.Length) then
    raise new System.ArgumentException('Points: array sizes mismatch');

  var classes := labels.Distinct.ToArray;
  &Array.Sort(classes);

  var pal := CurrentPalette;

  foreach var c in classes do
  begin
    var ind := labels.Indices(v -> v = c).ToArray;

    var xs := ind.ConvertAll(i -> x[i]);
    var ys := ind.ConvertAll(i -> y[i]);

    var clr :=
      if color<>DefaultColor
      then color
      else pal.Colors[c mod pal.Colors.Length];

    Points(xs, ys, clr, size, marker, nil);
  end;
end;

static procedure Plot.Heatmap(m: array[,] of real);
begin
  RunUI(() ->
  begin
    DrawHeatmap(rootChart, m, nil);
  end);
end;

static procedure Plot.Heatmap(m: array[,] of real; names: array of string);
begin
  RunUI(() ->
  begin
    DrawHeatmap(rootChart, m, names);
  end);
end;

// --- Vector overloads

{static procedure Plot.LineGraph(x, y: Vector;
  color: ColorWPF; thickness: real; legend: string);
begin
  LineGraph(x.Data, y.Data, color, thickness, legend);
end;


static procedure Plot.Points(x, y: Vector;
  color: ColorWPF; size: real; marker: MarkerType; legend: string);
begin
  Points(x.Data, y.Data, color, size, marker, legend);
end;


static procedure Plot.Points(x, y: Vector;
  labels: array of integer;
  color: ColorWPF; size: real; marker: MarkerType);
begin
  Points(x.Data, y.Data, labels, color, size, marker);
end;


static procedure Plot.Hist(x: Vector; bins: integer;
  color: ColorWPF; alpha: real; legend: string);
begin
  Hist(x.Data, bins, color, alpha, legend);
end;

// --- Matrix overloads
static procedure Plot.Heatmap(m: Matrix);
begin
  Heatmap(m.Data);
end;

static procedure Plot.PairPlot(X: Matrix; labels: array of integer; names: array of string);
begin
  PairPlot(X.Data, labels, names);
end;}

static function Plot.Grid(rows,cols: integer): Figure;
begin
  var fig: Figure;

  RunUI(() ->
  begin
    fig := new Figure(rows,cols);
    win.Content := fig.grid;
    gridMode := true;
  end);

  Result := fig;
end;

static procedure Plot.SetPalette(p: PaletteWPF);
begin
  RunUI(() ->
  begin
    if p = nil then exit;

    currentPalette := p;
  end);
end;

function HistogramCounts(x: array of real; bins: integer; xmin, xmax: real): array of integer;
begin
  var counts := new integer[bins];
  var w := (xmax-xmin)/bins;

  foreach var v in x do
  begin
    var k := trunc((v-xmin)/w);
    if k>=bins then k := bins-1;
    if k<0 then k := 0;
    counts[k] += 1;
  end;

  Result := counts;
end;

class procedure Plot.PairPlot(X: array[,] of real; labels: array of integer; names: array of string);
begin
  var n := names.Length;
  var bins := 20;

  var fig := Plot.Grid(n,n);

  var xmin := new real[n];
  var xmax := new real[n];
  var ymax := new real[n];

  // диапазоны
  for var j := 0 to n-1 do
  begin
    var col := X.Col(j);

    xmin[j] := Floor(col.Min);
    xmax[j] := Ceil(col.Max);

    var counts := HistogramCounts(col,bins,xmin[j],xmax[j]);
    ymax[j] := counts.Max*1.1;
  end;

  for var i := 0 to n-1 do
  for var j := 0 to n-1 do
  begin
    var ax := fig[i,j];

    if i=j then
    begin
      ax.Hist(X.Col(i),bins:=bins);

      ax.XLim(xmin[j],xmax[j]);
      ax.YLim(0,ymax[j]);
    end
    else
    begin
      var xs := X.Col(j);
      var ys := X.Col(i);
      
      if labels = nil then
        ax.Points(xs, ys, size := 3)
      else
        ax.Points(xs, ys, labels, size := 3);      

      ax.XLim(xmin[j],xmax[j]);
      ax.YLim(xmin[i],xmax[i]);
    end;

    // подписи только по краям
    if i=n-1 then
      ax.XLabel(names[j]);

    if j=0 then
      ax.YLabel(names[i]);
  end;
end;

class procedure Plot.EnsureAxes(chart: ChartWPF);
begin
  var container := chart.Content as GridWPF;

  if container = nil then
  begin
    container := new GridWPF;
    chart.Content := container;
  end;

  foreach var el in container.Children do
    if el is LineGraphWPF then
      exit;

  var g := new LineGraphWPF;
  g.StrokeThickness := 0;
  g.Plot(Arr(0.0, 1.0), Arr(0.0, 1.0));

  container.Children.Add(g);
end;

static procedure Plot.Hist(x: array of real; bins: integer;
  color: ColorWPF; alpha: real; legend: string);
begin
  RunUI(() ->
  begin
    Plot.DrawHist(rootChart, x, bins, color, alpha, legend);
  end);
end;

static procedure Plot.DrawHist(chart: ChartWPF; x: array of real;
  bins: integer; color: ColorWPF; alpha: real; legend: string);
begin
  EnsureAxes(chart);

  var hist := new HistogramPlot;

  if bins = 0 then
    bins := Round(Sqrt(x.Length));

  hist.BinsCount := bins;
  hist.Color := if color<>DefaultColor then color else NextRootColor;
  hist.Alpha := alpha;
  
  hist.SetData(x);
  // после этого известен hist.MaxCount

  if legend<>nil then
  begin
    hist.Description := legend;
    chart.LegendVisibility := Visibility.Visible;
  end;

  AddSeries(chart, hist);
  var xmin := Floor(x.Min);
  var xmax := Ceil(x.Max);
  
  chart.PlotOriginX := xmin;
  chart.PlotWidth := xmax - xmin;
  chart.PlotOriginY := 0;
  chart.PlotHeight := hist.MaxCount * 1.1;
end;

static procedure Plot.HistMany(arrays: array of array of real; bins: integer;
  colors: array of ColorWPF; alpha: real; legend: array of string);
begin
  RunUI(() ->
  begin
    Plot.DrawHistMany(rootChart, arrays, bins, colors, alpha, legend);
  end);
end;

static procedure Plot.DrawHistMany(chart: ChartWPF; arrays: array of array of real;
  bins: integer; colors: array of ColorWPF; alpha: real; legends: array of string);
begin
  EnsureAxes(chart);
  
  var hasLegend := (legends <> nil) and (legends.Length > 0);

  if hasLegend then
    chart.LegendVisibility := Visibility.Visible;

  if (arrays = nil) or (arrays.Length = 0) then exit;

  // --- общий диапазон ---
  var globalMin := real.MaxValue;
  var globalMax := real.MinValue;

  foreach var arr in arrays do
  begin
    if (arr = nil) or (arr.Length = 0) then continue;
    globalMin := Min(globalMin, arr.Min);
    globalMax := Max(globalMax, arr.Max);
  end;
  
  if globalMin = real.MaxValue then
    exit;
  
  if globalMax <= globalMin then
    exit;

  if bins = 0 then
  begin
    var maxLen := 0;
    
    foreach var arr in arrays do
      if (arr <> nil) and (arr.Length > maxLen) then
        maxLen := arr.Length;
    
    if maxLen = 0 then
      exit;
    
    bins := Round(Sqrt(maxLen));
  end;

  var maxCount := 0;

  // --- рисуем ---
  for var k := 0 to arrays.Length - 1 do
  begin
    var x := arrays[k];
    if (x = nil) or (x.Length = 0) then continue;

    var hist := new HistogramPlot;

    hist.BinsCount := bins;
    hist.Color := if (colors<>nil) and (k < colors.Length) then colors[k] else NextRootColor;
    hist.Alpha := alpha;

    // фиксируем диапазон
    hist.MinValue := globalMin;
    hist.MaxValue := globalMax;

    hist.SetData(x);

    if hist.MaxCount > maxCount then
      maxCount := hist.MaxCount;

    if hasLegend and (k < legends.Length) then
      hist.Description := legends[k];   

    AddSeries(chart, hist);
  end;

  // --- оси ---
  chart.PlotOriginX := Floor(globalMin);
  chart.PlotWidth   := Ceil(globalMax) - Floor(globalMin);
  chart.PlotOriginY := 0;
  chart.PlotHeight  := maxCount * 1.1;
end;

constructor HistogramPlot.Create;
begin
  fBins := new List<Polygon>;
  fColor := Colors.SteelBlue;
  fAlpha := 0.7;
  fBinsCount := 20;
  IsAutoFitEnabled := false;
end;

procedure HistogramPlot.SetData(x: array of real);
begin
  Children.Clear;
  fBins.Clear;

  if x=nil then exit;
  if x.Length=0 then exit;

  var xmin := if not real.IsNaN(MinValue) then Floor(MinValue) else Floor(x.Min);
  var xmax := if not real.IsNaN(MaxValue) then Ceil(MaxValue) else Ceil(x.Max);

  if xmax=xmin then exit;

  var w := (xmax-xmin)/fBinsCount;

  var counts := new integer[fBinsCount];

  foreach var v in x do
  begin
    var k := trunc((v-xmin)/w);
    if k>=fBinsCount then k := fBinsCount-1;
    if k<0 then k := 0;
    counts[k] += 1;
  end;
  

  var brush := new SolidColorBrush(fColor);
  brush.Opacity := fAlpha;

  for var i:=0 to fBinsCount-1 do
  begin
    var x0 := xmin + i*w;
    var x1 := x0 + w;
    var h := counts[i];

    var poly := new Polygon;

    var pts := new PointCollection;

    pts.Add(new Point(x0,0));
    pts.Add(new Point(x0,h));
    pts.Add(new Point(x1,h));
    pts.Add(new Point(x1,0));

    PlotWPF.SetPoints(poly, pts);

    poly.Fill := brush;
    poly.Stroke := Brushes.Black;
    poly.StrokeThickness := 0.5;

    fBins.Add(poly);
    Children.Add(poly);
  end;

  fMaxCount := counts.Max;
end;

constructor HeatmapPlot.Create;
begin
  IsAutoFitEnabled := false;
  fMinValue := 0;
  fMaxValue := 0;
end;

function HeatmapPlot.Clamp01(x: real): real;
begin
  if x < 0 then
    Result := 0
  else if x > 1 then
    Result := 1
  else
    Result := x;
end;

function HeatmapPlot.LerpColor(c1, c2: ColorWPF; t: real): ColorWPF;
begin
  t := Clamp01(t);

  Result := Color.FromRgb(
    byte(Round(c1.R + (c2.R - c1.R) * t)),
    byte(Round(c1.G + (c2.G - c1.G) * t)),
    byte(Round(c1.B + (c2.B - c1.B) * t))
  );
end;

function HeatmapPlot.ColorForValue(v: real): ColorWPF;
begin
  Result := HeatmapColor(v, fMinValue, fMaxValue);
end;

procedure HeatmapPlot.SetData(m: array[,] of real);
begin
  Children.Clear;
  fCells.Clear;

  if m = nil then
    exit;

  var rows := m.GetLength(0);
  var cols := m.GetLength(1);

  if (rows = 0) or (cols = 0) then
    exit;

  var foundFinite := false;
  fMinValue := 0;
  fMaxValue := 0;

  for var i := 0 to rows - 1 do
    for var j := 0 to cols - 1 do
    begin
      var v := m[i, j];
      if real.IsNaN(v) or real.IsInfinity(v) then
        continue;

      if not foundFinite then
      begin
        fMinValue := v;
        fMaxValue := v;
        foundFinite := true;
      end
      else
      begin
        if v < fMinValue then
          fMinValue := v;
        if v > fMaxValue then
          fMaxValue := v;
      end;
    end;

  if not foundFinite then
    exit;

  for var i := 0 to rows - 1 do
    for var j := 0 to cols - 1 do
    begin
      var v := m[i, j];
      var brush := new SolidColorBrush(ColorForValue(v));

      var x0 := j;
      var x1 := j + 1;

      // Делаем нулевую строку верхней строкой матрицы.
      var y0 := rows - i - 1;
      var y1 := rows - i;

      var poly := new Polygon;
      var pts := new PointCollection;

      pts.Add(new Point(x0, y0));
      pts.Add(new Point(x0, y1));
      pts.Add(new Point(x1, y1));
      pts.Add(new Point(x1, y0));

      PlotWPF.SetPoints(poly, pts);

      poly.Fill := brush;
      poly.Stroke := Brushes.LightGray;
      poly.StrokeThickness := 0.5;

      fCells.Add(poly);
      Children.Add(poly);
    end;
end;

procedure SurfacePlot.SetData(labels: array of integer;
  nx, ny: integer; xmin, xmax, ymin, ymax: real; pal: Palette);
begin
  Children.Clear;

  var dx := (xmax - xmin) / nx;
  var dy := (ymax - ymin) / ny;

  var k := 0;

  for var iy := 0 to ny - 1 do
  for var ix := 0 to nx - 1 do
  begin
    var lab := labels[k];
    k += 1;

    var clr := pal.Colors[Abs(lab) mod pal.Colors.Length];
    var brush := new SolidColorBrush(clr);

    var x0 := xmin + ix * dx;
    var y0 := ymin + iy * dy;

    var eps := dx * 0.01;

    var x1 := x0 + dx + eps;
    var y1 := y0 + dy + eps;

    var poly := new Polygon;

    var pts := new PointCollection;
    pts.Add(new Point(x0, y0));
    pts.Add(new Point(x0, y1));
    pts.Add(new Point(x1, y1));
    pts.Add(new Point(x1, y0));

    PlotWPF.SetPoints(poly, pts);

    poly.Fill := brush;
    poly.StrokeThickness := 0;

    Children.Add(poly);
  end;
end;

static procedure Plot.DrawSurface(chart: ChartWPF;
  labels: array of integer; nx, ny: integer;
  xmin, xmax, ymin, ymax: real;
  pal: Palette);
begin
  EnsureAxes(chart);

  var s := new SurfacePlot;
  s.SetData(labels, nx, ny, xmin, xmax, ymin, ymax, pal);

  AddSeries(chart, s);
end;

static function Plot.MakeGrid(xmin, xmax, ymin, ymax: real; nx, ny: integer): Matrix;
begin
  Result := new Matrix(nx * ny, 2);
  
  var k := 0;
  for var iy := 0 to ny - 1 do
  begin
    var y := ymin + (ymax - ymin) * iy / (ny - 1);
    for var ix := 0 to nx - 1 do
    begin
      var x := xmin + (xmax - xmin) * ix / (nx - 1);
      Result[k, 0] := x;
      Result[k, 1] := y;
      k += 1;
    end;
  end;
end;

procedure Cell.Surface(x1, x2: array of real; nx, ny: integer; f: Matrix -> array of integer; pal: PlotML.Palette);
begin
  Plot.RunUI(() ->
  begin
    EnsureChart;

    var xmin := x1.Min - 0.5;
    var xmax := x1.Max + 0.5;
    var ymin := x2.Min - 0.5;
    var ymax := x2.Max + 0.5;

    var G := Plot.MakeGrid(xmin, xmax, ymin, ymax, nx, ny);
    var labels := f(G);
    
    var usePal := if pal <> nil then pal else palette;

    Plot.DrawSurface(chart, labels, nx, ny, xmin, xmax, ymin, ymax, usePal);
  end);
end;

static procedure Plot.Surface(labels: array of integer; nx, ny: integer; xmin, xmax, ymin, ymax: real; pal: PlotML.Palette);
begin
  RunUI(() ->
  begin
    var usePal := if pal <> nil then pal else CurrentPalette;
    DrawSurface(rootChart, labels, nx, ny, xmin, xmax, ymin, ymax, usePal);
  end);
end;

static procedure Plot.Surface(x1, x2: array of real; nx, ny: integer; f: Matrix -> array of integer; pal: PlotML.Palette);
begin
  RunUI(() ->
  begin
    var xmin := x1.Min - 0.5;
    var xmax := x1.Max + 0.5;
    var ymin := x2.Min - 0.5;
    var ymax := x2.Max + 0.5;

    var G := MakeGrid(xmin, xmax, ymin, ymax, nx, ny);
    var labels := f(G);
    
    var usePal := if pal <> nil then pal else CurrentPalette;

    DrawSurface(rootChart, labels, nx, ny, xmin, xmax, ymin, ymax, usePal);
  end);
end;

initialization
  InitUI;

end.
