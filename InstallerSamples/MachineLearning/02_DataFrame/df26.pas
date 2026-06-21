uses MLABC;

begin
  var text := '''
id,created_at,name
1,15.01.2024,Alice
2,16.01.2024 12:30:00,Bob
3,17.01.2024 09:15:00,Charlie
''';

  var df := DataFrame.FromCsvText(text);

  var filtered := df.Filter(cur -> cur.DateTime('created_at') >= DateTime.Create(2024, 1, 16));

  Println('Исходный DataFrame:');
  df.Print;
  Println;
  Println('Дата >= 16.01.2024:');
  filtered.Print;
end.
