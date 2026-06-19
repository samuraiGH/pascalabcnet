uses MLABC;

begin
  var df := DataFrame.FromCsvText('''
name,age,income
Alice,20,100
Bob,,120
Clara,25,
Dmitry,30,150
''');

  Println('Пропусков в столбце age:', df.MissingCount('age'));
  Println('Пропусков в столбце income:', df.MissingCount('income'));
  Println;

  Println('Пропуски по столбцам:');
  df.MissingCounts.Print;
  Println;

  Println('Строки без пропусков во всех столбцах:');
  df.DropMissing.Print;
  Println;

  Println('Строки без пропусков в age и income:');
  df.DropMissing(['age', 'income']).Print;
end.
