uses MLABC, DataFrameABC;

begin
  var text := '''
id,created_at,name
1,15.01.2023 10:20:30,Alice
2,16.02.2024 12:30:00,Bob
3,05.03.2024 09:15:45,Charlie
''';

  var df := DataFrame.FromCsvText(text);

  var df2 := df.WithDatePart('created_at', 'year', dpYear);
  var df3 := df2.WithDateParts('created_at', [
    ('month', dpMonth),
    ('day', dpDay),
    ('date_only', dpDate)
  ]);

  df3.Print;
end.
