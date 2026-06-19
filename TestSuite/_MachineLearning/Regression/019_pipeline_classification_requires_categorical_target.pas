uses MLABC;
uses TestHelpers in '..\TestHelpers.pas';

begin
  var df := new DataFrame;
  df.AddFloatColumn('X', Arr(1.0, 2.0, 3.0, 4.0));
  df.AddStrColumn('Target', Arr('cat', 'dog', 'cat', 'dog'));

  var pipe := DataPipeline.BuildClassification(
    'Target',
    Arr($'X'),
    new LogisticRegression
  );

  pipe.Fit(df);

  var pred := pipe.Predict(df);
  var labels := pipe.PredictLabels(df);
  var classes := pipe.GetClassLabels;

  Check(pred.Length = df.RowCount, 'Pipeline prediction length mismatch');
  Check(labels.Length = df.RowCount, 'Pipeline label prediction length mismatch');
  Check(classes.Length = 2, 'Pipeline class count mismatch');
end.

