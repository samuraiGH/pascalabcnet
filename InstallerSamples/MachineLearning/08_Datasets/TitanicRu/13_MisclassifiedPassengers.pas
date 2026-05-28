uses MLABC;

function LabelText(v: integer): string;
begin
  Result := if v = 1 then 'выжил' else 'не выжил';
end;

begin
  var ds := Datasets.TitanicRu;
  var features := ['Класс', 'Пол', 'Возраст', 'БратьяИСупруги', 'РодителиИДети', 'ЦенаБилета', 'ПортПосадки'];

  var fullDf := ds.Data;
  var (trainFull, testFull) :=
    fullDf.StratifiedTrainTestSplit(ds.Target, testRatio := 0.2, seed := 42);

  var trainDf := trainFull.Drop(['Id', 'Имя']);
  var testDf := testFull.Drop(['Id', 'Имя']);

  var pipe :=
    DataPipeline.BuildClassification(
      ds.Target,
      features,
      new Imputer(['Возраст']),
      new Imputer('Саутгемптон', ['ПортПосадки']),
      new OneHotEncoder('Пол'),
      new OneHotEncoder('ПортПосадки'),
      new StandardScaler,
      new RandomForestClassifier(nTrees := 100, maxDepth := 6, minSamplesLeaf := 3, minSamplesSplit := 6)
    );

  pipe.Fit(trainDf);

  var pred := pipe.Predict(testDf);
  var y := pipe.GetEncodedLabels(testDf);
  var testPrepared := pipe.Transform(testDf);
  var cur := testFull.GetCursor;
  var curPrepared := testPrepared.GetCursor;

  Println('TitanicRu: пассажиры, классифицированные неправильно');
  Println;

  var shown := 0;
  while cur.MoveNext do
  begin
    if not curPrepared.MoveNext then
      break;

    var i := cur.Position;
    if y[i] <> pred[i] then
    begin
      var id := cur.Int('Id');
      var name := cur.Str('Имя');
      var sex := cur.Str('Пол');
      var pclass := cur.Int('Класс');
      var age := curPrepared.Float('Возраст');
      var fare := cur.Float('ЦенаБилета');
      
      Println($'Id={id}, {name}');
      Println($'  Пол={sex}, класс={pclass}, возраст={age:F1}, билет={fare:F1}');
      Println($'  Истина: {LabelText(y[i])}, прогноз: {LabelText(pred[i])}');
      Println;

      shown += 1;
      if shown >= 10 then break;
    end;
  end;
end.
