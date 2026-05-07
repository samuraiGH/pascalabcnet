uses MLABC;

begin
  var ds := Datasets.TitanicRu;
  var df := ds.Data.Drop(['Id', 'Имя']);

  // Заполняем пропуски.
  var ageImputer := new Imputer(['Возраст']);
  df := ageImputer.FitTransform(df);

  var portImputer := new Imputer('Саутгемптон', ['ПортПосадки']);
  df := portImputer.FitTransform(df);

  // Кодируем категориальные признаки числами.
  var sexEncoder := new LabelEncoder('Пол');
  df := sexEncoder.FitTransform(df);

  var portEncoder := new LabelEncoder('ПортПосадки');
  df := portEncoder.FitTransform(df);

  var features := ['Класс', 'Пол', 'Возраст', 'БратьяИСупруги', 'РодителиИДети', 'ЦенаБилета', 'ПортПосадки'];
  var X := df.ToMatrix(features);
  var y := df.GetIntColumn('Выжил');

  var (Xtrain, Xtest, ytrain, ytest) := Validation.TrainTestSplit(X, y, testRatio := 0.2, seed := 42);

  var scaler := new StandardScaler;
  scaler.Fit(Xtrain);
  var XtrainScaled := scaler.Transform(Xtrain);
  var XtestScaled := scaler.Transform(Xtest);

  var lr := new LogisticRegression(learningRate := 0.01, epochs := 2000);
  lr.Fit(XtrainScaled, ytrain);
  var predLR := lr.Predict(XtestScaled);

  var tree := new DecisionTreeClassifier(maxDepth := 5, minSamplesLeaf := 3, minSamplesSplit := 6);
  tree.Fit(Xtrain, ytrain);
  var predTree := tree.Predict(Xtest);

  var forest := new RandomForestClassifier(nTrees := 100, maxDepth := 6, minSamplesLeaf := 3, minSamplesSplit := 6);
  forest.Fit(Xtrain, ytrain);
  var predForest := forest.Predict(Xtest);

  Println('Сравнение моделей на TitanicRu');
  Println($'LogisticRegression:    Accuracy = {Metrics.Accuracy(ytest, predLR):F3}');
  Println($'DecisionTreeClassifier: Accuracy = {Metrics.Accuracy(ytest, predTree):F3}');
  Println($'RandomForestClassifier: Accuracy = {Metrics.Accuracy(ytest, predForest):F3}');
end.
