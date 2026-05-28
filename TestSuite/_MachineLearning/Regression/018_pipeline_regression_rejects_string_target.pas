uses MLABC;
uses TestHelpers in '..\TestHelpers.pas';

begin
  var df := new DataFrame;
  df.AddFloatColumn('X', Arr(1.0, 2.0, 3.0));
  df.AddStrColumn('Target', Arr('low', 'mid', 'high'));
  df := df.SetCategorical(['Target']);

  var pipe := DataPipeline.BuildRegression(
    'Target',
    Arr($'X'),
    new LinearRegression
  );

  CheckRaises(procedure -> begin pipe.Fit(df); end,
    'Regression pipeline must reject non-numeric target');
end.

