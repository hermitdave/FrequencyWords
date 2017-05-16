const globby = require('globby');
const fs = require('fs');

globby('../../content/**/**/*.txt').then(paths => {
    paths.forEach(function(filename) {
        var words = fs.readFileSync(filename, 'utf8');
        console.log(`converting ${filename}`);
        fs.writeFileSync(filename.replace('.txt', '.csv'), `word,frequency\n${words.replace(/ /g,',')}`, 'utf8');
        console.log(`done ${filename}`);
    }, this);;
});