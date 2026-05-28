uses MLABC;
uses TestHelpers in '..\TestHelpers.pas';

begin
  CheckRaises(procedure -> begin
    var pipe := DataPipeline.BuildClassification(
      'Target',
      Arr($'X', $'Target'),
      new LogisticRegression
    );
  end,
  'DataPipeline.Build must reject target inside features');
end.

