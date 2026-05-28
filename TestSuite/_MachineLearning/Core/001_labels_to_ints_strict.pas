uses MLABC;
uses TestHelpers in '..\TestHelpers.pas';

begin
  var ds := new Dataset;
  ds.Task := TaskType.Classification;
  ds.Target := 'Target';
  ds.Data := new DataFrame;
  ds.Data.AddStrColumn('Target', Arr('cat', 'dog', 'cat'));

  var enc := new LabelEncoder;
  var y := enc.FitTransform(ds);

  Check(y.Length = 3, 'Length mismatch');
  Check(y[0] = 0, 'First label mismatch');
  Check(y[1] = 1, 'Second label mismatch');
  Check(y[2] = 0, 'Third label mismatch');
  Check(enc.Classes.Length = 2, 'Class count mismatch');
  Check(enc.Classes[0] = 'cat', 'First class mismatch');
  Check(enc.Classes[1] = 'dog', 'Second class mismatch');
end.
