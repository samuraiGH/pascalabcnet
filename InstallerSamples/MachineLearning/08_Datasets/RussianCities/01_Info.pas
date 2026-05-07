uses MLABC;
 
begin
  var ds := Datasets.RussianCities;
  
  var df := ds.Data;

  df.Schema.Println;
  df.PrintInfo;
  Println;
  df.Print;
end.