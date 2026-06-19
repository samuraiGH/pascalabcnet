uses MLABC;

begin
  var text := '''
id,created_at,name
1,2024-01-15,Alice
2,2024-01-16 12:30:00,Bob
''';

  var schema := new Dictionary<string, ColumnType>;
  schema['created_at'] := ColumnType.ctDateTime;

  var df1 := CsvLoader.LoadFromLines(
    text.ToLines,
    schema := schema
  );

  Println('Явная схема:');
  df1.Print;
  Println(df1.DateTime('created_at')[0].Year);

  var df2 := CsvLoader.LoadFromLines(
    text.ToLines,
    inferTypes := True
  );

  Println;
  Println('Автоопределение:');
  df2.Print(dateTimeFormat := 'dd.MM.yy');
  Println(df2.GetColumnType('created_at'));
end.
