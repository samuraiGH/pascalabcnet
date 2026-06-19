uses MLABC;

begin
  var df := DataFrame.FromCsvText('''
name,age,score
Alice,10,1.5
Bob,25,4.8
Alice,10,1.5
Clara,30,8.2
''');

  Println('Без полных дубликатов:');
  df.DropDuplicates.Print;
  Println;

  Println('После Clip(score, 0, 5):');
  df.Clip('score', 0, 5).Print;
end.
