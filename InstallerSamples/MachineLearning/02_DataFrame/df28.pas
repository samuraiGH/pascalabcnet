uses MLABC;

begin
  var text := '''
id,created_at,name
1,15.01.2023,Alice
2,16.01.2024 12:30:00,Bob
3,20.02.2023,Charlie
4,05.03.2024 09:15:00,Diana
''';

  var df := CsvLoader.LoadFromLines(
    text.ToLines,
    inferTypes := True
  );

  var df2 := df.WithColumnInt(
    'year',
    cur -> cur.DateTime('created_at').Year
  );

  var grouped := df2.GroupBy('year').Count;

  Println('DataFrame с колонкой year:');
  df2.Print;
  Println;
  Println('GroupBy(year).Count:');
  grouped.Print;
end.
