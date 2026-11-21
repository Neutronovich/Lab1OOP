grammar Expr;




compileUnit : expression EOF ;


programUnit : block EOF ;

block : statement (SEMI statement)* SEMI? ;

statement
    : name=VAR ASSIGN value=expression                                      # AssignStmt
    | IF cond=expression THEN thenBlock=block (ELSE elseBlock=block)? END   # IfStmt
    | WHILE cond=expression DO body=block END                               # WhileStmt
    ;

expression
    : LPAREN expression RPAREN                       # ParenExpr
    | op=(PLUS | MINUS) expression                   # UnaryExpr
    | expression op=(EQ | LT | GT) expression        # CompareExpr
    | NOT expression                                 # NotExpr
    | expression AND expression                      # AndExpr
    | expression OR expression                       # OrExpr
    | expression EQV expression                      # EqvExpr
    | NUMBER                                         # NumberExpr
    | CELL                                           # CellExpr
    | (TRUE | FALSE)                                 # BoolExpr
    | VAR                                            # VarExpr
    ;


NOT   : [Nn][Oo][Tt] ;
AND   : [Aa][Nn][Dd] ;
OR    : [Oo][Rr] ;
EQV   : [Ee][Qq][Vv] ;
TRUE  : [Tt][Rr][Uu][Ee] ;
FALSE : [Ff][Aa][Ll][Ss][Ee] ;
IF    : [Ii][Ff] ;
THEN  : [Tt][Hh][Ee][Nn] ;
ELSE  : [Ee][Ll][Ss][Ee] ;
END   : [Ee][Nn][Dd] ;
WHILE : [Ww][Hh][Ii][Ll][Ee] ;
DO    : [Dd][Oo] ;

ASSIGN : ':=' ;
SEMI   : ';' ;
PLUS   : '+' ;
MINUS  : '-' ;
EQ     : '=' ;
LT     : '<' ;
GT     : '>' ;
LPAREN : '(' ;
RPAREN : ')' ;

NUMBER : [0-9]+ ;                       
CELL   : [A-Za-z]+ [1-9] [0-9]* ;       
VAR    : [A-Za-z_]+ ;                   

WS : [ \t\r\n]+ -> skip ;
