uses MLABC;
uses TestHelpers in '..\TestHelpers.pas';

begin
  var ds := Datasets.Iris;
  var X := ds.Data.ToMatrix(ds.Features);

  var classes: array of string;
  var y := ds.Data.EncodeLabels(ds.Target, classes);

  var model := new RandomForestClassifier(20, maxDepth := 6, seed := 1);
  model.Fit(X, y);

  var pred := model.Predict(X);
  var labels := model.PredictLabels(X);
  var modelClasses := model.GetClassLabels;

  Check(modelClasses.Length = classes.Length, 'class count mismatch');
  Check(pred.Length = X.RowCount, 'Predict length mismatch');
  Check(labels.Length = X.RowCount, 'PredictLabels length mismatch');

  for var i := 0 to X.RowCount - 1 do
  begin
    var pi := pred[i];
    Check((pi >= 0) and (pi < modelClasses.Length), $'Predict[{i}] out of range');
    Check(labels[i] = pi.ToString, $'Predict[{i}] and PredictLabels[{i}] are inconsistent');
  end;
end.
