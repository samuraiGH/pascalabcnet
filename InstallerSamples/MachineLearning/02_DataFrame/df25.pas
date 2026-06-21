uses MLABC;

begin
  var text := '''
id,created_at,name
1,16.01.2024 12:30:00,Bob
2,15.01.2024,Alice
3,17.01.2024 09:15:00,Charlie
''';

  var df := DataFrame.FromCsvText(text);

  var sorted := df.SortBy('created_at');

  Println('Исходный DataFrame:');
  df.Print;
  Println;
  Println('После SortBy(created_at):');
  sorted.Print;
end.
