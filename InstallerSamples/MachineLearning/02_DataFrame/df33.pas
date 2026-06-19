uses MLABC;

begin
  var df := DataFrame.FromCsvText('''
Город
Москва
Казань
Москва
Омск
Казань
Москва
''');

  var values := df.Unique('Город');
  Println('Уникальные города:');
  foreach var v in values do
    Println(v.Str);

  Println;
  Println('Число различных городов: ', df.NUnique('Город'));
  Println;
  df.ValueCounts('Город').Print;
end.
