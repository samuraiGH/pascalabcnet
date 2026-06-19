uses MLABC;

begin
  var df := DataFrame.FromCsvText('''
product,count,pack_price
milk,2,120
bread,1,55
milk,3,118
tea,1,340
''');

  var total := df.Sum(r -> r['count'] * r['pack_price']);

  Println('Общая стоимость упаковок:', total:0:2);
end.
