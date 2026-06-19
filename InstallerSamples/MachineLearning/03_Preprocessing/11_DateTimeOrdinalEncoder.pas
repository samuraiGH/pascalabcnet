uses MLABC;

begin
  var df := DataFrame.FromCsvText('''
id,created_at,name
1,15.01.2024 00:00:00,Alice
2,16.01.2024 12:00:00,Bob
3,18.01.2024 06:00:00,Charlie
''');

  var enc1 := new DateTimeOrdinalEncoder('created_at');
  var df1 := enc1.FitTransform(df);

  Println('Смещение в днях от минимальной даты:');
  df1.Print;
  Println;

  var enc2 := new DateTimeOrdinalEncoder('created_at', 'created_at_hours', dtuHours);
  var df2 := enc2.FitTransform(df);

  Println('Смещение в часах от минимальной даты:');
  df2.Print;
end.
