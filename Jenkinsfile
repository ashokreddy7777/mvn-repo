pipeline{
  agent{label 'lin'}
  options{timeout (time: 1, unit:'HOURS')}
  tools{
    maven 'maven'
    jdk 'java'
  }
  stages{
    stage('Build'){
      steps{
        sh '''
            echo "PATH = ${PATH}"
            echo "M2_HOME = ${M2_HOME}"
            mvn -X clean install
        '''     
      }
    }
    stage('Tomcat Deploy'){
      steps{
        sh '''
           cp /home/ak/jenkins_home/workspace/a-automation/mvn-repo/f9.war /opt/tomcat/webapps/
           /opt/tomcat/bin/startup.sh
        '''    
      }
    }
    stage('ws cleanup'){
      steps{
        cleanWs()
      }
    }
  }
}
