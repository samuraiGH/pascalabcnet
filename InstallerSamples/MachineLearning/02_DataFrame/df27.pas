uses MLABC;

begin
  var text := '''
id,created_at,name
1,15.01.2024,Alice
2,16.01.2024 12:30:00,Bob
3,15.01.2024,Charlie
4,16.01.2024 12:30:00,Diana
''';

  var df := CsvLoader.LoadFromLines(
    text.ToLines,
    inferTypes := True
  );

  var grouped := df.GroupBy('created_at').Count;

  Println('Исходный DataFrame:');
  df.Print;
  Println;
  Println('GroupBy(created_at).Count:');
  grouped.Print;
end.
