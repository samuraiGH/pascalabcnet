uses MLABC;

begin
  var df := DataFrame.FromCsvText('''
product,count,price
milk,2,120
bread,1,55
milk,3,118
tea,1,340
coffee,2,260
''');

  Println('Средний чек по строке:', df.Mean(r -> r['count'] * r['price']):0:2);
  Println('Медианный чек по строке:', df.Median(r -> r['count'] * r['price']):0:2);
  Println('Минимальный чек по строке:', df.Min(r -> r['count'] * r['price']):0:2);
  Println('Максимальный чек по строке:', df.Max(r -> r['count'] * r['price']):0:2);
  Println('Стандартное отклонение:', df.Std(r -> r['count'] * r['price']):0:2);
end.
