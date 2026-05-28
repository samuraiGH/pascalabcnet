uses MLABC;
uses TestHelpers in '..\TestHelpers.pas';

begin
  var df := new DataFrame;
  df.AddBoolColumn('Flag', Arr(true, false, true, false));
  df.AddBoolColumn('IsNew', Arr(false, false, true, true));
  df.AddFloatColumn('X', Arr(1.5, 2.5, 3.5, 4.5));
  df.AddIntColumn('Target', Arr(0, 1, 0, 1));
  df := df.SetCategorical(['Target']);

  var X := df.ToMatrix(['Flag', 'IsNew', 'X']);
  var y := df.ToVector('Target').ToIntArray;

  Check(X.RowCount = 4, 'ToMatrix row count mismatch');
  Check(X.ColCount = 3, 'ToMatrix col count mismatch');

  Check(Abs(X[0,0] - 1.0) < 1e-12, 'Flag=true must encode as 1');
  Check(Abs(X[1,0] - 0.0) < 1e-12, 'Flag=false must encode as 0');
  Check(Abs(X[0,1] - 0.0) < 1e-12, 'IsNew=false must encode as 0');
  Check(Abs(X[2,1] - 1.0) < 1e-12, 'IsNew=true must encode as 1');
  Check(Abs(X[3,2] - 4.5) < 1e-12, 'Numeric column must stay unchanged');

  var model := new DecisionTreeClassifier(maxDepth := 3);
  model.Fit(X, y);
  var pred := model.Predict(X);

  Check(pred.Length = df.RowCount, 'Manual bool-feature model prediction length mismatch');
end.
