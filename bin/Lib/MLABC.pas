/// Основной модуль библиотеки машинного обучения.
/// Объединяет модели, метрики, валидацию и вспомогательные компоненты.
unit MLABC;

// =============================================================
// СТАТИСТИЧЕСКАЯ ПОЛИТИКА БИБЛИОТЕКИ ML PascalABC.NET
//
// В библиотеке используются следующие соглашения:
//
// 1. DataFrame (описательная статистика):
//    • дисперсия вычисляется с делением на (n - 1)
//
// 2. LinearAlgebra и ML:
//    • дисперсия вычисляется с делением на n
//
// 3. PCA:
//    • ковариационная матрица вычисляется с делением на (n - 1)
//
// Это соответствует распространённой практике:
//   • описательная статистика — выборочная дисперсия
//   • алгоритмы ML — дисперсия генеральной совокупности
// =============================================================

// =============================================================
// КОНВЕЙЕРЫ
//
// Табличные:
//   DataPipeline
//   ClassificationDataPipeline
//   RegressionDataPipeline
//   ClusteringDataPipeline
//
// Матричные:
//   MatrixPipeline
//   ClassificationMatrixPipeline
//   RegressionMatrixPipeline
//   ClusteringMatrixPipeline
//
// DataPipeline и MatrixPipeline используются как фасады
// для построения специализированных конвейеров.
// =============================================================

// =============================================================
// ПОЛИТИКА PREDICT
//
// 1. Классификация:
//    • Predict возвращает метки классов в том же виде,
//      в каком они были поданы модели при обучении.
//    • PredictLabels возвращает строковые метки классов.
//
// 2. Регрессия:
//    • Predict возвращает вектор числовых предсказаний.
//
// 3. Кластеризация:
//    • Predict и FitPredict возвращают номера кластеров.
//
// Внутреннее кодирование классов может использоваться внутри модели,
// но наружу через Predict оно не выдаётся.
// =============================================================

interface 

uses LinearAlgebraML;
uses ValidationML;
uses MLCoreABC;
uses MLModelsABC;
uses MetricsABC;
uses PreprocessorABC;
uses DataFrameABC;
uses DataFrameABCCore;
uses MLExceptions;
uses InspectionML;
uses MLPipelineABC;
uses MLDatasets;
uses DataAdapters;
uses MLUtilsABC;

type 
  Vector = LinearAlgebraML.Vector;
  Matrix = LinearAlgebraML.Matrix;
  
  Validation = ValidationML.Validation;
  GridSearch = ValidationML.GridSearch;
  
  Metrics = MetricsABC.Metrics;
  ClassificationMetrics = MetricsABC.ClassificationMetrics;
  RegressionMetrics = MetricsABC.RegressionMetrics;
  ClusteringMetrics = MetricsABC.ClusteringMetrics;
  ConfusionMatrix = MetricsABC.ConfusionMatrix;
  
  DataPipeline = MLPipelineABC.DataPipeline;
  ClassificationDataPipeline = MLPipelineABC.ClassificationDataPipeline;
  RegressionDataPipeline = MLPipelineABC.RegressionDataPipeline;
  ClusteringDataPipeline = MLPipelineABC.ClusteringDataPipeline;
  
  DataFrame = DataFrameABC.DataFrame;
  DataFrameCursor = DataFrameABCCore.DataFrameCursor;
  ColumnType = DataFrameABCCore.ColumnType;
  Column = DataFrameABCCore.Column;
  
  Statistics = DataFrameABC.Statistics;
  CsvLoader = DataFrameABC.CsvLoader;
  JoinKind = DataFrameABC.JoinKind;
  GroupView = DataFrameABC.GroupView;
  
  IProbabilisticClassifier = MLCoreABC.IProbabilisticClassifier;
  IClassifier = MLCoreABC.IClassifier;
  IRegressor = MLCoreABC.IRegressor;

  StandardScaler = MLModelsABC.StandardScaler;
  PCATransformer = MLModelsABC.PCATransformer;
  MinMaxScaler = MLModelsABC.MinMaxScaler;
  VarianceThreshold = MLModelsABC.VarianceThreshold;
  SelectKBest = MLModelsABC.SelectKBest;
  FeatureScore = MLModelsABC.FeatureScore;
  Normalizer = MLModelsABC.Normalizer;
  
  NormType = MLModelsABC.NormType;
  
  Activations = MLModelsABC.Activations;
  MatrixPipeline = MLModelsABC.MatrixPipeline;
  ClassificationMatrixPipeline = MLModelsABC.ClassificationMatrixPipeline;
  RegressionMatrixPipeline = MLModelsABC.RegressionMatrixPipeline;
  ClusteringMatrixPipeline = MLModelsABC.ClusteringMatrixPipeline;
  
  LinearRegression = MLModelsABC.LinearRegression;
  LogisticRegression = MLModelsABC.LogisticRegression;
  RidgeRegression = MLModelsABC.RidgeRegression;
  LassoRegression = MLModelsABC.LassoRegression;
  ElasticNet = MLModelsABC.ElasticNet;
  DecisionTreeClassifier = MLModelsABC.DecisionTreeClassifier;
  DecisionTreeRegressor = MLModelsABC.DecisionTreeRegressor;
  RandomForestRegressor = MLModelsABC.RandomForestRegressor;
  RandomForestClassifier = MLModelsABC.RandomForestClassifier;
  GradientBoostingRegressor = MLModelsABC.GradientBoostingRegressor;
  GradientBoostingClassifier = MLModelsABC.GradientBoostingClassifier;
  KNNClassifier = MLModelsABC.KNNClassifier;
  KNNRegressor = MLModelsABC.KNNRegressor;
  KMeans = MLModelsABC.KMeans;
  DBSCAN = MLModelsABC.DBSCAN;
  
  KNNWeighting = MLModelsABC.KNNWeighting;
  TGBLoss = MLModelsABC.TGBLoss;
  TMaxFeaturesMode = MLModelsABC.TMaxFeaturesMode;

  MLException = MLExceptions.MLException;
  MLNotFittedException = MLExceptions.MLNotFittedException;
  MLDimensionException = MLExceptions.MLDimensionException;
  
  Inspection = InspectionML.Inspection;
  
  IPreprocessor = PreprocessorABC.IPreprocessor;
  OrdinalEncoder = PreprocessorABC.OrdinalEncoder;
  OneHotEncoder = PreprocessorABC.OneHotEncoder;
  ImputeStrategy = PreprocessorABC.ImputeStrategy;
  Imputer = PreprocessorABC.Imputer;
  
  Datasets = MLDatasets.Datasets;
  Dataset = MLDatasets.Dataset;
  LabelEncoder = MLDatasets.LabelEncoder;
  TaskType = MLDatasets.TaskType;
  
  IModel = MLCoreABC.IModel;
  IUnsupervisedModel = MLCoreABC.IUnsupervisedModel;
  
  TaskKind = MLPipelineABC.TaskKind;
  
  AggregationKind = DataFrameABC.AggregationKind;
  
const
  akMean = AggregationKind.akMean;
  akMin = AggregationKind.akMin;
  akMax = AggregationKind.akMax;
  akCount = AggregationKind.akCount;
  akSum = AggregationKind.akSum;
  akStd = AggregationKind.akStd;
  
  /// Внутреннее соединение
  jkInner = JoinKind.jkInner;
  jkLeft = JoinKind.jkLeft;
  jkRight = JoinKind.jkRight;
  jkFull = JoinKind.jkFull;

  /// Кодирует строковые метки классов в целочисленные индексы.
  /// Каждому уникальному значению присваивается номер 0,1,2,...
  /// Порядок кодирования соответствует порядку первого появления меток.
  /// Используется при обучении моделей и визуализации.
  /// Предполагается, что входные данные уже очищены от пропущенных значений.
  function EncodeLabels(labels: array of string): array of integer;

  
implementation

function EncodeLabels(labels: array of string): array of integer := MLUtilsABC.EncodeLabels(labels);
  
end.
