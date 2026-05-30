uses DataFrameABC;

begin
  var df1 := DataFrame.FromCsvText('''
id,name,age
1,Ann,10
2,Bob,12
''');

  var df2 := DataFrame.FromCsvText('''
id,name,age
3,Cat,11
4,Dan,13
''');

  var df := DataFrame.Concat(df1, df2);
  df.Print;
end.
