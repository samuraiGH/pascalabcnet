uses MLABC;

begin
  var df := DataFrame.FromCsvText('''
id,created_at,name
1,15.01.2024 00:00:00,Alice
2,15.01.2024 06:00:00,Bob
3,15.01.2024 12:00:00,Charlie
4,15.01.2024 18:00:00,Diana
5,15.01.2024 23:56:00,Eva
''');

  var enc := new DateTimeCyclicEncoder('created_at', dpTimeOfDay);
  var df2 := enc.FitTransform(df);

  df2.Print;
end.
