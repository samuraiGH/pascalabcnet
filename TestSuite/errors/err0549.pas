type
  Matr = class
    static function operator implicit(a: array [,] of integer): Matr;
    begin
      
    end;
  end;


begin
  var a: array of integer;
  var m: Matr;
  m := a;
end.