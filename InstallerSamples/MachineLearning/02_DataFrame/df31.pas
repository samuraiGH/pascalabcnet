uses MLABC;

begin
  var df := DataFrame.FromCsvText('''
name,district,operation,amount
Alice,North,sale,120
Bob,North,refund,30
Clara,South,sale,210
Dmitry,North,sale,180
''');

  var saleCount := df.Count(r ->
    (r['district'] = 'North') and
    (r['operation'] = 'sale')
  );

  Println('Продаж в районе North:', saleCount);
end.
