<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet xmlns:xsl = "http://www.w3.org/1999/XSL/Transform" version = "1.0" > 

<xsl:template match = "/" > 
<html>
<head>
<title></title>
<style>
body { 
  font-family: "Times Ext Roman", "Indic Times", "Doulos SIL", Tahoma, "Arial Unicode MS", Gentium;
  background: white;
}

p {
  border-top: 0in; border-bottom: 0in;
  padding-top: 0in; padding-bottom: 0in;
  margin-top: 0in; margin-bottom: 0.0cm;
}
</style>
</head>
<body>
<xsl:apply-templates select="/*"/>
</body>
</html>
</xsl:template>

<xsl:template match='word'>
<p>
<xsl:apply-templates/>
</p>
</xsl:template>

</xsl:stylesheet>