uses MLABC;

begin
  var df := DataFrame.FromCsvText('''
id,created_at,name
1,15.01.2024 10:20:30,Alice
2,16.02.2024 12:30:00,Bob
3,17.03.2025 09:15:45,Charlie
''');

  var enc1 := new DateTimeComponentsEncoder('created_at');
  var df1 := enc1.FitTransform(df);

  Println('Стандартные компоненты:');
  df1.Print;
  Println;

  var enc2 := new DateTimeComponentsEncoder('created_at', [dpYear, dpMonth, dpDay, dpHour]);
  var df2 := enc2.FitTransform(df);

  Println('Пользовательский набор компонент:');
  df2.Print;
end.
